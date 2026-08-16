using System.Collections.Generic;
using System.Text;
using Agxmeister.Uplink.Compilation;
using Agxmeister.Uplink.Console;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    /// <summary>
    /// The read half of the compile cycle: the verb that looks and, whatever it finds, changes nothing.
    /// </summary>
    [TestFixture]
    public sealed class CompileStatusEndpointTests
    {
        private sealed class WatchedCompiler : ICompiler
        {
            private readonly CompileResult result;

            public WatchedCompiler(CompileResult result)
            {
                this.result = result;
            }

            public int Looks { get; private set; }

            public CompileResult Poll(bool force)
            {
                throw new AssertionException("looking must never start or collect a run");
            }

            public CompileResult Peek()
            {
                Looks++;
                return result;
            }
        }

        private static CompileResult Running()
        {
            return new CompileResult
            {
                State = CompileLog.Compiling,
                Errors = new List<CompileMessage>(),
                Warnings = new List<CompileMessage>(),
            };
        }

        private static CompileResult Waiting()
        {
            return new CompileResult
            {
                State = CompileLog.Done,
                Changed = true,
                Stale = true,
                Errors = new List<CompileMessage>
                {
                    new CompileMessage
                    {
                        File = "Assets/Player.cs",
                        Line = 12,
                        Column = 5,
                        Message = "; expected",
                        Assembly = "Assembly-CSharp",
                        Level = CompileLevel.Error,
                    },
                },
                Warnings = new List<CompileMessage>(),
                ErrorCount = 1,
                DurationMs = 2400,
                IsPlaying = true,
                Note = "Play mode was on.",
                // Filled in so the field-versus-description assertion below covers it.
                Console = new ConsolePage
                {
                    Entries = new List<ConsoleEntry>(),
                    NextSince = 7,
                    Truncated = false,
                    HistoryAvailable = false,
                    Counts = new ConsoleCounts(),
                },
            };
        }

        private static CompileResult AtRest()
        {
            return new CompileResult
            {
                State = CompileLog.Idle,
                Stale = true,
                Errors = new List<CompileMessage>(),
                Warnings = new List<CompileMessage>(),
            };
        }

        [Test]
        public void AnswersAcceptedWhileABuildIsGoing()
        {
            var endpoint = new CompileStatusEndpoint(new WatchedCompiler(Running()));

            var response = endpoint.Handle(Requests.Of("GET", "/compile"));
            var body = JObject.Parse(Encoding.UTF8.GetString(response.Body));

            Assert.AreEqual(202, response.Status, "the same 'not finished' signal the acting call gives");
            Assert.AreEqual("compiling", body["state"].Value<string>());
        }

        [Test]
        public void ShowsAFinishedResultWithoutTakingDeliveryOfIt()
        {
            var endpoint = new CompileStatusEndpoint(new WatchedCompiler(Waiting()));

            var response = endpoint.Handle(Requests.Of("GET", "/compile"));
            var body = JObject.Parse(Encoding.UTF8.GetString(response.Body));

            Assert.AreEqual(200, response.Status);
            Assert.AreEqual("done", body["state"].Value<string>());
            Assert.AreEqual("Assets/Player.cs", body["errors"][0]["file"].Value<string>());
            Assert.IsTrue(body["stale"].Value<bool>(), "a read is not the hand-over");
        }

        [Test]
        public void ReportsTheRestingStateTheActingCallNeverShows()
        {
            var endpoint = new CompileStatusEndpoint(new WatchedCompiler(AtRest()));

            var response = endpoint.Handle(Requests.Of("GET", "/compile"));
            var body = JObject.Parse(Encoding.UTF8.GetString(response.Body));

            Assert.AreEqual(200, response.Status);
            Assert.AreEqual("idle", body["state"].Value<string>(), "nothing running, nothing waiting");
        }

        [Test]
        public void LooksAsOftenAsItIsAskedAndActsNoneOfTheTimes()
        {
            var compiler = new WatchedCompiler(AtRest());
            var endpoint = new CompileStatusEndpoint(compiler);

            for (var i = 0; i < 10; i++)
            {
                Assert.AreEqual(200, endpoint.Handle(Requests.Of("GET", "/compile")).Status);
            }

            // `Poll` throws, so ten calls that got this far never started a build.
            Assert.AreEqual(10, compiler.Looks);
        }

        [Test]
        public void DescribesEveryFieldItActuallyReturns()
        {
            var endpoint = new CompileStatusEndpoint(new WatchedCompiler(Waiting()));

            var body = JObject.Parse(
                Encoding.UTF8.GetString(endpoint.Handle(Requests.Of("GET", "/compile")).Body));
            var described = JObject.FromObject(endpoint.Describe())
                ["responses"]["200"]["content"]["application/json"]["schema"]["properties"];

            foreach (var field in body)
            {
                Assert.IsNotNull(described[field.Key], string.Format("'{0}' is returned but not described.", field.Key));
            }
        }

        [Test]
        public void DescribesTheThreeStatesItCanReport()
        {
            var described = JObject.FromObject(new CompileStatusEndpoint(new WatchedCompiler(AtRest())).Describe());
            var states = described
                ["responses"]["200"]["content"]["application/json"]["schema"]["properties"]["state"]["enum"];

            CollectionAssert.AreEquivalent(
                new[] { "idle", "compiling", "done" }, states.ToObject<string[]>());
            Assert.IsNotNull(described["responses"]["202"], "202 is answered, so it must be described");
        }

        [Test]
        public void TakesNoInputAtAll()
        {
            var described = JObject.FromObject(new CompileStatusEndpoint(new WatchedCompiler(AtRest())).Describe());

            Assert.IsNull(described["requestBody"], "a GET that reads a body would be a POST in disguise");
            Assert.IsNull(described["parameters"]);
        }
    }
}
