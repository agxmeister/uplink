using System;
using Agxmeister.Uplink.Compilation;
using Agxmeister.Uplink.Persistence;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    /// <summary>
    /// The self-polling cycle, which is what makes a tool out of an operation that outlives its own request.
    /// </summary>
    [TestFixture]
    public sealed class CompileLogTests
    {
        private static readonly DateTime Start = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

        private static CompileMessage Error(string file, int line)
        {
            return new CompileMessage
            {
                File = file,
                Line = line,
                Message = "; expected",
                Assembly = "Assembly-CSharp",
                Level = CompileLevel.Error,
            };
        }

        /// <summary>A whole run: asked for, started, one error, finished.</summary>
        private static CompileLog Failed()
        {
            var log = new CompileLog();
            log.Advance(Start);
            log.Started(Start);
            log.Add(Error("Assets/Player.cs", 12));
            log.Completed(Start.AddSeconds(3));
            return log;
        }

        [Test]
        public void FirstCallStartsABuildAndSaysSo()
        {
            var log = new CompileLog();

            var outcome = log.Advance(Start);

            Assert.IsTrue(outcome.ShouldTrigger);
            Assert.AreEqual(CompileLog.Compiling, outcome.Result.State);
        }

        [Test]
        public void FurtherCallsDuringABuildChangeNothing()
        {
            var log = new CompileLog();
            log.Advance(Start);
            log.Started(Start);

            var outcome = log.Advance(Start.AddSeconds(1));

            Assert.IsFalse(outcome.ShouldTrigger, "a second call must not start a second build");
            Assert.AreEqual(CompileLog.Compiling, outcome.Result.State);
        }

        [Test]
        public void ReportsTheOutcomeOnceTheBuildIsFinished()
        {
            var result = Failed().Advance(Start.AddSeconds(4)).Result;

            Assert.AreEqual(CompileLog.Done, result.State);
            Assert.IsTrue(result.Changed);
            Assert.AreEqual(1, result.ErrorCount);
            Assert.AreEqual("Assets/Player.cs", result.Errors[0].File);
            Assert.AreEqual(12, result.Errors[0].Line);
            Assert.AreEqual(3000, result.DurationMs);
        }

        [Test]
        public void HandsTheOutcomeOverOnceAndThenBuildsAgain()
        {
            var log = Failed();

            var first = log.Advance(Start.AddSeconds(4));
            var second = log.Advance(Start.AddSeconds(5));

            Assert.AreEqual(CompileLog.Done, first.Result.State);
            Assert.IsFalse(first.ShouldTrigger);
            Assert.AreEqual(
                CompileLog.Compiling, second.Result.State, "the call after a result means 'build again'");
            Assert.IsTrue(second.ShouldTrigger);
        }

        [Test]
        public void DoesNotReportTheLastRunsErrorsAgainstOneThatHasNotStarted()
        {
            var log = Failed();
            log.Advance(Start.AddSeconds(4));

            var started = log.Advance(Start.AddSeconds(5));

            Assert.AreEqual(CompileLog.Compiling, started.Result.State);
            Assert.AreEqual(0, started.Result.ErrorCount, "those errors belong to the previous build");
        }

        [Test]
        public void KeepsStandingErrorsWhenNothingNeededRebuilding()
        {
            var log = Failed();
            log.Advance(Start.AddSeconds(4));

            // A second run that never starts: the compiler had nothing to do.
            log.Advance(Start.AddSeconds(5));
            Assert.IsTrue(log.GaveUpWaiting(Start.AddSeconds(11), TimeSpan.FromSeconds(5), false));
            log.Completed(Start.AddSeconds(11));

            var result = log.Advance(Start.AddSeconds(12)).Result;

            Assert.IsFalse(result.Changed, "nothing was rebuilt");
            Assert.AreEqual(1, result.ErrorCount, "the error already standing is still the truth");
        }

        [Test]
        public void DiscardsTheOldMessagesOnceARealBuildBegins()
        {
            var log = Failed();
            log.Advance(Start.AddSeconds(4));

            log.Advance(Start.AddSeconds(5));
            log.Started(Start.AddSeconds(5));
            log.Completed(Start.AddSeconds(6));

            var result = log.Advance(Start.AddSeconds(7)).Result;

            Assert.IsTrue(result.Changed);
            Assert.AreEqual(0, result.ErrorCount);
        }

        [Test]
        public void WaitsForTheCompilerBeforeConcludingThereIsNothingToDo()
        {
            var log = new CompileLog();
            log.Advance(Start);
            var grace = TimeSpan.FromSeconds(5);

            Assert.IsFalse(log.GaveUpWaiting(Start.AddSeconds(1), grace, false), "too early to conclude anything");
            Assert.IsFalse(
                log.GaveUpWaiting(Start.AddSeconds(30), grace, true),
                "a busy Editor is on its way to the same compile");
            Assert.IsTrue(log.GaveUpWaiting(Start.AddSeconds(30), grace, false));
        }

        [Test]
        public void StopsWaitingOnceTheCompilerStarts()
        {
            var log = new CompileLog();
            log.Advance(Start);
            log.Started(Start.AddSeconds(1));

            Assert.IsFalse(log.GaveUpWaiting(Start.AddSeconds(30), TimeSpan.FromSeconds(5), false));
        }

        [Test]
        public void SurvivesTheDomainReloadTheBuildItselfCauses()
        {
            var store = new InMemoryStore();
            var before = new CompileLog();
            before.Advance(Start);
            before.Started(Start);
            before.Add(Error("Assets/Enemy.cs", 7));
            Stored.Write(store, "compile", before.Capture());

            // The reload happens here: every static is gone, and the run is picked up on the other side.
            var after = new CompileLog();
            after.Restore(Stored.Read<CompileState>(store, "compile"));
            after.Completed(Start.AddSeconds(2));

            var result = after.Advance(Start.AddSeconds(3)).Result;

            Assert.AreEqual(CompileLog.Done, result.State);
            Assert.AreEqual(1, result.ErrorCount);
            Assert.AreEqual("Assets/Enemy.cs", result.Errors[0].File);
        }

        [Test]
        public void ReportsTheTrueTotalWhenThereAreMoreMessagesThanItWillList()
        {
            var log = new CompileLog();
            log.Advance(Start);
            log.Started(Start);
            for (var i = 0; i < 150; i++)
            {
                log.Add(Error("Assets/Player.cs", i));
            }
            log.Completed(Start.AddSeconds(1));

            var result = log.Advance(Start.AddSeconds(2)).Result;

            Assert.AreEqual(100, result.Errors.Count);
            Assert.AreEqual(150, result.ErrorCount);
        }
    }
}
