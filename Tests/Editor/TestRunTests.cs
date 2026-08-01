using System;
using System.Text;
using Agxmeister.Uplink.Http;
using Agxmeister.Uplink.Persistence;
using Agxmeister.Uplink.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    [TestFixture]
    public sealed class TestRunTests
    {
        private static readonly DateTime Start = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

        private sealed class StubRunner : ITestRunner
        {
            private readonly TestRun run;

            public StubRunner(TestRun run)
            {
                this.run = run;
            }

            public TestRunOptions Asked { get; private set; }

            public TestRun Poll(TestRunOptions options)
            {
                Asked = options;
                return run;
            }
        }

        private static TestOutcome Outcome(string name, string status)
        {
            return new TestOutcome
            {
                Name = name,
                Status = status,
                Message = status == TestState.Failed ? "Expected 2 but was 3" : null,
                StackTrace = status == TestState.Failed ? "at Maths.Add()" : null,
                DurationMs = 12,
            };
        }

        /// <summary>A whole run: asked for, three results, finished.</summary>
        private static TestLog Finished()
        {
            var log = new TestLog();
            log.Advance(new TestRunOptions(), Start);
            log.Add(Outcome("Maths.Adds", TestState.Passed));
            log.Add(Outcome("Maths.Subtracts", TestState.Failed));
            log.Add(Outcome("Maths.Divides", TestState.Skipped));
            log.Completed(Start.AddSeconds(4));
            return log;
        }

        [Test]
        public void FirstCallStartsARunAndSaysSo()
        {
            var report = new TestLog().Advance(new TestRunOptions { Mode = TestModes.Play }, Start);

            Assert.IsTrue(report.ShouldStart);
            Assert.AreEqual(TestLog.Running, report.Run.State);
            Assert.AreEqual(TestModes.Play, report.Run.Mode);
        }

        [Test]
        public void FurtherCallsDuringARunChangeNothing()
        {
            var log = new TestLog();
            log.Advance(new TestRunOptions(), Start);

            var report = log.Advance(new TestRunOptions(), Start.AddSeconds(1));

            Assert.IsFalse(report.ShouldStart, "a second call must not start a second run");
            Assert.AreEqual(TestLog.Running, report.Run.State);
        }

        [Test]
        public void ReportsFailuresAndCountsEverything()
        {
            var run = Finished().Advance(new TestRunOptions(), Start.AddSeconds(5)).Run;

            Assert.AreEqual(TestLog.Done, run.State);
            Assert.AreEqual(1, run.Summary.Passed);
            Assert.AreEqual(1, run.Summary.Failed);
            Assert.AreEqual(1, run.Summary.Skipped);
            Assert.AreEqual(3, run.Summary.Total);
            Assert.AreEqual(4000, run.Summary.DurationMs);
            Assert.AreEqual(1, run.Failures.Count);
            Assert.AreEqual("Maths.Subtracts", run.Failures[0].Name);
        }

        [Test]
        public void ListsOnlyFailuresUnlessAskedForEverything()
        {
            var log = Finished();

            Assert.IsNull(log.Advance(new TestRunOptions(), Start.AddSeconds(5)).Run.Tests);
        }

        [Test]
        public void ListsEveryTestWhenAsked()
        {
            var log = Finished();

            var run = log.Advance(new TestRunOptions { IncludePassed = true }, Start.AddSeconds(5)).Run;

            Assert.AreEqual(3, run.Tests.Count);
        }

        [Test]
        public void HandsTheOutcomeOverOnceAndThenRunsAgain()
        {
            var log = Finished();

            var first = log.Advance(new TestRunOptions(), Start.AddSeconds(5));
            var second = log.Advance(new TestRunOptions(), Start.AddSeconds(6));

            Assert.AreEqual(TestLog.Done, first.Run.State);
            Assert.IsFalse(first.ShouldStart);
            Assert.AreEqual(TestLog.Running, second.Run.State, "the call after a result means 'run again'");
            Assert.IsTrue(second.ShouldStart);
            Assert.AreEqual(0, second.Run.Summary.Total, "a fresh run starts with no results");
        }

        [Test]
        public void EndsTheCycleWhenTheRunCouldNotHappenAtAll()
        {
            var log = new TestLog();
            log.Advance(new TestRunOptions(), Start);

            log.Failed("Scripts have compile errors.", Start.AddSeconds(1));
            var run = log.Advance(new TestRunOptions(), Start.AddSeconds(2)).Run;

            Assert.AreEqual(TestLog.Done, run.State, "a run that cannot start must not report 'running' forever");
            Assert.AreEqual("Scripts have compile errors.", run.Error);
        }

        [Test]
        public void SurvivesTheDomainReloadAPlayModeRunCauses()
        {
            var store = new InMemoryStore();
            var before = new TestLog();
            before.Advance(new TestRunOptions { Mode = TestModes.Play }, Start);
            before.Add(Outcome("Maths.Adds", TestState.Passed));
            Stored.Write(store, "tests", before.Capture());

            // The reload lands here, halfway through the suite.
            var after = new TestLog();
            after.Restore(Stored.Read<TestRunState>(store, "tests"));
            after.Add(Outcome("Maths.Subtracts", TestState.Failed));
            after.Completed(Start.AddSeconds(9));

            var run = after.Advance(new TestRunOptions(), Start.AddSeconds(10)).Run;

            Assert.AreEqual(TestModes.Play, run.Mode);
            Assert.AreEqual(2, run.Summary.Total, "results from both halves of the run");
            Assert.AreEqual(1, run.Failures.Count);
        }

        [Test]
        public void AnswersAcceptedWhileTheRunIsStillGoing()
        {
            var endpoint = new TestsEndpoint(new StubRunner(new TestLog()
                .Advance(new TestRunOptions(), Start).Run));

            Assert.AreEqual(202, endpoint.Handle(Requests.Of("POST", "/tests")).Status);
        }

        [Test]
        public void RunsTheEditModeSuiteWhenTheBodyDoesNotSayOtherwise()
        {
            var runner = new StubRunner(Finished().Advance(new TestRunOptions(), Start.AddSeconds(5)).Run);

            new TestsEndpoint(runner).Handle(Requests.Of("POST", "/tests"));

            Assert.AreEqual(TestModes.Edit, runner.Asked.Mode);
        }

        [Test]
        public void PassesTheFilterThrough()
        {
            var runner = new StubRunner(Finished().Advance(new TestRunOptions(), Start.AddSeconds(5)).Run);
            var body = "{\"mode\":\"play\",\"assemblies\":[\"Game.Tests\"],\"includePassed\":true}";

            new TestsEndpoint(runner).Handle(Requests.Of("POST", "/tests", body));

            Assert.AreEqual(TestModes.Play, runner.Asked.Mode);
            CollectionAssert.AreEqual(new[] { "Game.Tests" }, runner.Asked.Assemblies);
            Assert.IsTrue(runner.Asked.IncludePassed);
        }

        [Test]
        public void RefusesASuiteItDoesNotHave()
        {
            var endpoint = new TestsEndpoint(new StubRunner(new TestRun()));

            Assert.Throws<BadRequestException>(
                () => endpoint.Handle(Requests.Of("POST", "/tests", "{\"mode\":\"player\"}")));
        }

        [Test]
        public void DescribesEveryFieldItActuallyReturns()
        {
            var run = Finished().Advance(new TestRunOptions { IncludePassed = true }, Start.AddSeconds(5)).Run;
            var endpoint = new TestsEndpoint(new StubRunner(run));

            var body = JObject.Parse(Encoding.UTF8.GetString(
                endpoint.Handle(Requests.Of("POST", "/tests")).Body));
            var described = JObject.FromObject(endpoint.Describe())
                ["responses"]["200"]["content"]["application/json"]["schema"]["properties"];

            foreach (var field in body)
            {
                Assert.IsNotNull(described[field.Key], string.Format("'{0}' is returned but not described.", field.Key));
            }

            var outcome = described["failures"]["items"]["properties"];
            foreach (var field in (JObject)body["failures"][0])
            {
                Assert.IsNotNull(
                    outcome[field.Key], string.Format("failure '{0}' is returned but not described.", field.Key));
            }

            var summary = described["summary"]["properties"];
            foreach (var field in (JObject)body["summary"])
            {
                Assert.IsNotNull(
                    summary[field.Key], string.Format("summary '{0}' is returned but not described.", field.Key));
            }
        }
    }
}
