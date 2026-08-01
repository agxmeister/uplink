using System.Collections.Generic;
using System.Text;
using Agxmeister.Uplink.Compilation;
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

            public CompileResult Poll()
            {
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

        private static CompileResult Finished()
        {
            return new CompileResult
            {
                State = CompileLog.Done,
                Changed = true,
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
    }
}
