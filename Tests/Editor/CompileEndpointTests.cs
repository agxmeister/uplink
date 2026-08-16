using System.Collections.Generic;
using System.Text;
using Agxmeister.Uplink.Compilation;
using Agxmeister.Uplink.Console;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    [TestFixture]
    public sealed class CompileEndpointTests
    {
        private sealed class StubCompiler : ICompiler
        {
            private readonly CompileResult result;

            public StubCompiler(CompileResult result)
            {
                this.result = result;
            }

            public bool? AskedToForce { get; private set; }

            public CompileResult Poll(bool force)
            {
                AskedToForce = force;
                return result;
            }

            public CompileResult Peek()
            {
                throw new AssertionException("the acting call must not merely look");
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

        private static CompileResult Finished()
        {
            return new CompileResult
            {
                State = CompileLog.Done,
                Changed = true,
                Forced = false,
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

        [Test]
        public void AnswersAcceptedWhileTheBuildIsStillGoing()
        {
            var endpoint = new CompileEndpoint(new StubCompiler(Running()));

            Assert.AreEqual(202, endpoint.Handle(Requests.Of("POST", "/compile")).Status);
        }

        [Test]
        public void AnswersOkWithTheOutcomeOnceItIsFinished()
        {
            var endpoint = new CompileEndpoint(new StubCompiler(Finished()));

            var response = endpoint.Handle(Requests.Of("POST", "/compile"));
            var body = JObject.Parse(Encoding.UTF8.GetString(response.Body));

            Assert.AreEqual(200, response.Status);
            Assert.AreEqual("done", body["state"].Value<string>());
            Assert.AreEqual("Assets/Player.cs", body["errors"][0]["file"].Value<string>());
            Assert.AreEqual(12, body["errors"][0]["line"].Value<int>());
        }

        [Test]
        public void DescribesEveryFieldItActuallyReturns()
        {
            var endpoint = new CompileEndpoint(new StubCompiler(Finished()));

            var body = JObject.Parse(
                Encoding.UTF8.GetString(endpoint.Handle(Requests.Of("POST", "/compile")).Body));
            var described = JObject.FromObject(endpoint.Describe())
                ["responses"]["200"]["content"]["application/json"]["schema"]["properties"];

            foreach (var field in body)
            {
                Assert.IsNotNull(described[field.Key], string.Format("'{0}' is returned but not described.", field.Key));
            }

            var message = described["errors"]["items"]["properties"];
            foreach (var field in (JObject)body["errors"][0])
            {
                Assert.IsNotNull(
                    message[field.Key], string.Format("error '{0}' is returned but not described.", field.Key));
            }
        }

        [Test]
        public void DescribesTheAcceptedAnswerItCanGive()
        {
            var described = JObject.FromObject(new CompileEndpoint(new StubCompiler(Running())).Describe());

            Assert.IsNotNull(described["responses"]["202"], "202 is answered, so it must be described");
        }

        [Test]
        public void AsksForAForcedReloadWhenTheBodySaysSo()
        {
            var compiler = new StubCompiler(Running());

            new CompileEndpoint(compiler).Handle(Requests.Of("POST", "/compile", "{\"force\": true}"));

            Assert.IsTrue(compiler.AskedToForce.Value);
        }

        [Test]
        public void TreatsAMissingBodyAsAnOrdinaryRun()
        {
            var compiler = new StubCompiler(Running());

            new CompileEndpoint(compiler).Handle(Requests.Of("POST", "/compile"));

            Assert.IsFalse(compiler.AskedToForce.Value);
        }

        [Test]
        public void DescribesTheForceOptionItReads()
        {
            var described = JObject.FromObject(new CompileEndpoint(new StubCompiler(Running())).Describe());

            Assert.IsNotNull(
                described["requestBody"]["content"]["application/json"]["schema"]["properties"]["force"],
                "'force' is read but not described.");
        }
    }
}
