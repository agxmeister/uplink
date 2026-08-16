using System;
using Agxmeister.Uplink.Compilation;
using Agxmeister.Uplink.Console;
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
        public void LookingAtAnIdleCycleStartsNothing()
        {
            var log = new CompileLog();

            for (var i = 0; i < 10; i++)
            {
                var seen = log.Observe();
                Assert.AreEqual(CompileLog.Idle, seen.State, "nothing is running and nothing is waiting");
                Assert.IsTrue(seen.Stale.Value);
            }

            Assert.IsTrue(log.Advance(Start).ShouldTrigger, "the cycle was left where it was: this starts a run");
        }

        [Test]
        public void LookingAtAFinishedRunDoesNotTakeDeliveryOfIt()
        {
            var log = Failed();

            var seen = log.Observe();
            Assert.AreEqual(CompileLog.Done, seen.State, "a result is waiting for whoever posts next");
            Assert.AreEqual(1, seen.ErrorCount);
            Assert.IsTrue(seen.Stale.Value);
            Assert.AreEqual(CompileLog.Done, log.Observe().State, "looking twice sees the same result");

            var handed = log.Advance(Start.AddSeconds(4));

            Assert.AreEqual(CompileLog.Done, handed.Result.State, "the hand-over still had its result to give");
            Assert.IsNull(handed.Result.Stale, "the hand-over is not a read");
            Assert.IsFalse(handed.ShouldTrigger);
        }

        [Test]
        public void LookingAfterTheHandOverStillShowsWhatTheRunFound()
        {
            var log = Failed();
            log.Advance(Start.AddSeconds(4));

            var seen = log.Observe();

            Assert.AreEqual(CompileLog.Idle, seen.State, "the result was collected: the next post builds again");
            Assert.AreEqual(1, seen.ErrorCount, "the error already standing is still the truth");
            Assert.AreEqual(3000, seen.DurationMs);
            Assert.IsTrue(seen.Stale.Value);
        }

        [Test]
        public void LookingWhileARunIsGoingReportsProgressAndNothingStale()
        {
            var log = new CompileLog();
            log.Advance(Start);
            log.Started(Start);

            var seen = log.Observe();

            Assert.AreEqual(CompileLog.Compiling, seen.State);
            Assert.IsNull(seen.Stale, "progress is live, not a result someone else was given");
            Assert.IsNull(seen.Console, "the run's log belongs to its outcome");
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
        public void DoesNotReportDoneUntilThePromisedReloadHasHappened()
        {
            var log = new CompileLog();
            log.Advance(Start);
            log.Started(Start);
            // The build succeeded, so a domain reload is coming; done must wait for it.
            log.ExpectReload(Start.AddSeconds(2));

            Assert.AreEqual(CompileLog.Compiling, log.Advance(Start.AddSeconds(3)).Result.State);

            log.Reloaded(Start.AddSeconds(4), false);
            var result = log.Advance(Start.AddSeconds(5)).Result;

            Assert.AreEqual(CompileLog.Done, result.State);
            Assert.AreEqual(4000, result.DurationMs, "the run lasts through its reload");
        }

        [Test]
        public void AForcedRunReportsItWasForcedAndThatNothingChanged()
        {
            var log = new CompileLog();

            var outcome = log.Advance(Start, true);
            Assert.IsTrue(outcome.ShouldTrigger);

            // Nothing needed rebuilding, so the compiler never starts; the requested reload still happens.
            log.ExpectReload(Start.AddSeconds(1));
            log.Reloaded(Start.AddSeconds(2), false);

            var result = log.Advance(Start.AddSeconds(3)).Result;

            Assert.AreEqual(CompileLog.Done, result.State);
            Assert.IsTrue(result.Forced);
            Assert.IsFalse(result.Changed, "a forced reload is not a rebuild");
        }

        [Test]
        public void DoesNotConcludeThereWasNothingToDoWhileAReloadIsPromised()
        {
            var log = new CompileLog();
            log.Advance(Start, true);
            log.ExpectReload(Start.AddSeconds(1));

            Assert.IsFalse(
                log.GaveUpWaiting(Start.AddSeconds(30), TimeSpan.FromSeconds(5), false),
                "a forced run that never compiles is still waiting for its reload, not finished");
        }

        [Test]
        public void StopsWaitingOnAReloadThatNeverComes()
        {
            var log = new CompileLog();
            log.Advance(Start);
            log.Started(Start);
            log.ExpectReload(Start.AddSeconds(2));

            var grace = TimeSpan.FromSeconds(15);
            Assert.IsFalse(log.GaveUpOnReload(Start.AddSeconds(10), grace, false), "too early");
            Assert.IsFalse(log.GaveUpOnReload(Start.AddSeconds(60), grace, true), "a busy Editor may still reload");
            Assert.IsTrue(log.GaveUpOnReload(Start.AddSeconds(60), grace, false));
        }

        [Test]
        public void HandsOverTheConsoleMessagesTheRunProduced()
        {
            var console = new ConsoleBuffer();
            console.Record(ConsoleLevel.Log, "from before the run", null, Start);
            var log = new CompileLog(console);

            log.Advance(Start);
            console.Record(ConsoleLevel.Log, "Stage 3: menu arrows rebuilt", null, Start.AddSeconds(1));
            console.Record(ConsoleLevel.Log, "[Uplink] Serving http://localhost:8787/", null, Start.AddSeconds(2));
            log.Started(Start);
            log.ExpectReload(Start.AddSeconds(2));
            log.Reloaded(Start.AddSeconds(3), false);

            var result = log.Advance(Start.AddSeconds(4)).Result;

            Assert.AreEqual(1, result.Console.Entries.Count, "only the run's own messages, minus Uplink's chatter");
            Assert.AreEqual("Stage 3: menu arrows rebuilt", result.Console.Entries[0].Message);
            Assert.AreEqual(1, result.Console.Counts.Logs, "counts drop what was filtered too");
        }

        [Test]
        public void SaysNothingAboutTheConsoleWhileTheRunIsStillGoing()
        {
            var log = new CompileLog(new ConsoleBuffer());

            Assert.IsNull(log.Advance(Start).Result.Console);
        }

        [Test]
        public void WarnsWhenTheReloadRanDuringPlayMode()
        {
            var log = new CompileLog();
            log.Advance(Start, true);
            log.ExpectReload(Start.AddSeconds(1));
            log.Reloaded(Start.AddSeconds(2), true);

            var result = log.Advance(Start.AddSeconds(3)).Result;

            Assert.IsNotNull(result.Note);
            StringAssert.Contains("play mode", result.Note);
        }

        [Test]
        public void ARunThatCrossedItsReloadSaysSoOnTheOtherSide()
        {
            var store = new InMemoryStore();
            var before = new CompileLog();
            before.Advance(Start);
            before.Started(Start);
            before.ExpectReload(Start.AddSeconds(2));
            Stored.Write(store, "compile", before.Capture());

            // The reload happens here; the far side must know to close the run out.
            var after = new CompileLog();
            after.Restore(Stored.Read<CompileState>(store, "compile"));

            Assert.IsTrue(after.CrossedReload);

            after.Reloaded(Start.AddSeconds(3), false);
            Assert.IsFalse(after.CrossedReload);
            Assert.AreEqual(CompileLog.Done, after.Advance(Start.AddSeconds(4)).Result.State);
        }

        [Test]
        public void AFreshRunHasNotCrossedAnything()
        {
            var log = new CompileLog();
            log.Advance(Start);

            Assert.IsFalse(log.CrossedReload, "asked for but not started: no reload of ours has happened");
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
