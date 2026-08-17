using System.Collections.Generic;
using System.Text;
using Agxmeister.Uplink.Controls;
using Agxmeister.Uplink.Http;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    [TestFixture]
    public sealed class InputEndpointTests
    {
        /// <summary>
        /// Stands in for the Input System assembly, which may not even be compiled. Everything the endpoint
        /// is responsible for — shape, status codes, the described contract — is on this side of the seam.
        /// </summary>
        internal sealed class StubDriver : IInputDriver
        {
            private readonly InputResult answer;
            private readonly BadRequestException refusal;

            public StubDriver() : this(new InputResult { State = InputScript.Running, Steps = 2 })
            {
            }

            public StubDriver(InputResult answer)
            {
                this.answer = answer;
            }

            public StubDriver(BadRequestException refusal)
            {
                this.refusal = refusal;
                answer = new InputResult { State = InputScript.Running };
            }

            public IList<InputStep> Asked { get; private set; }

            public bool WasAsked { get; private set; }

            public InputResult Poll(IList<InputStep> steps)
            {
                WasAsked = true;
                Asked = steps;
                if (refusal != null)
                {
                    throw refusal;
                }
                return answer;
            }

            public InputResult Peek()
            {
                return answer;
            }
        }

        private static Request Script(string steps)
        {
            return Requests.Of("POST", "/input", "{\"steps\":" + steps + "}");
        }

        private static Response Play(StubDriver driver, string steps)
        {
            return new InputEndpoint(driver).Handle(Script(steps));
        }

        [Test]
        public void PassesTheStepsThroughAsWritten()
        {
            var driver = new StubDriver();

            Play(driver, "[{\"key\":\"space\",\"hold\":0.05},{\"wait\":0.5},{\"move\":[960,540]}]");

            Assert.AreEqual(3, driver.Asked.Count);
            Assert.AreEqual("space", driver.Asked[0].Key);
            Assert.AreEqual(0.05, driver.Asked[0].Hold.Value, 1e-9);
            Assert.AreEqual(0.5, driver.Asked[1].Wait.Value, 1e-9);
            CollectionAssert.AreEqual(new[] { 960f, 540f }, driver.Asked[2].Move);
        }

        [Test]
        public void ReadsAnEmptyBodyAsAPollRatherThanAScript()
        {
            var driver = new StubDriver();

            new InputEndpoint(driver).Handle(Requests.Of("POST", "/input", "{}"));

            Assert.IsTrue(driver.WasAsked);
            Assert.IsNull(driver.Asked, "no steps means poll, and the driver decides what that means");
        }

        [Test]
        public void AnswersAcceptedWhileItPlaysAndOkWhenItIsDone()
        {
            var playing = new InputEndpoint(new StubDriver(new InputResult { State = InputScript.Running }));
            var finished = new InputEndpoint(new StubDriver(new InputResult { State = InputScript.Done }));

            Assert.AreEqual(202, playing.Handle(Requests.Of("POST", "/input", "{}")).Status);
            Assert.AreEqual(200, finished.Handle(Requests.Of("POST", "/input", "{}")).Status);
        }

        [Test]
        public void LetsTheDriversRefusalThrough()
        {
            var endpoint = new InputEndpoint(new StubDriver(new BadRequestException("Not in play mode.")));

            Assert.Throws<BadRequestException>(
                () => endpoint.Handle(Script("[{\"key\":\"space\"}]")));
        }

        [Test]
        public void RefusesAStepThatSaysMoreThanOneThing()
        {
            var driver = new StubDriver();

            Assert.Throws<BadRequestException>(
                () => Play(driver, "[{\"key\":\"space\",\"wait\":0.5}]"));
            Assert.Throws<BadRequestException>(
                () => Play(driver, "[{\"key\":\"space\",\"click\":\"left\"}]"));
            Assert.IsFalse(driver.WasAsked, "a malformed script never reaches the Editor");
        }

        [Test]
        public void RefusesAStepThatSaysNothingAtAll()
        {
            Assert.Throws<BadRequestException>(() => Play(new StubDriver(), "[{}]"));
            Assert.Throws<BadRequestException>(() => Play(new StubDriver(), "[{\"hold\":0.5}]"));
        }

        [Test]
        public void RefusesAHoldWithNothingToHold()
        {
            var thrown = Assert.Throws<BadRequestException>(
                () => Play(new StubDriver(), "[{\"move\":[10,10],\"hold\":0.5}]"));

            StringAssert.Contains("wait", thrown.Message, "the message must point at the right parameter");
        }

        [Test]
        public void RefusesAPointerItCannotAim()
        {
            Assert.Throws<BadRequestException>(() => Play(new StubDriver(), "[{\"move\":[10]}]"));
            Assert.Throws<BadRequestException>(() => Play(new StubDriver(), "[{\"move\":[10,20,30]}]"));
            Assert.Throws<BadRequestException>(() => Play(new StubDriver(), "[{\"move\":[-1,20]}]"));
        }

        [Test]
        public void RefusesATimeItWillNotWaitFor()
        {
            Assert.Throws<BadRequestException>(() => Play(new StubDriver(), "[{\"wait\":-1}]"));
            Assert.Throws<BadRequestException>(() => Play(new StubDriver(), "[{\"wait\":600}]"));
            Assert.Throws<BadRequestException>(
                () => Play(new StubDriver(), "[{\"key\":\"space\",\"hold\":600}]"));
        }

        [Test]
        public void RefusesAScriptThatWouldRunForTooLong()
        {
            var steps = new List<string>();
            for (var i = 0; i < 20; i++)
            {
                steps.Add("{\"wait\":20}");
            }

            Assert.Throws<BadRequestException>(
                () => Play(new StubDriver(), "[" + string.Join(",", steps.ToArray()) + "]"));
        }

        [Test]
        public void RefusesMoreStepsThanItWillPlay()
        {
            var steps = new List<string>();
            for (var i = 0; i < InputEndpoint.MaxSteps + 1; i++)
            {
                steps.Add("{\"key\":\"space\",\"hold\":0}");
            }

            Assert.Throws<BadRequestException>(
                () => Play(new StubDriver(), "[" + string.Join(",", steps.ToArray()) + "]"));
        }

        [Test]
        public void DescribesEveryStepFieldItAccepts()
        {
            var described = JObject.FromObject(new InputEndpoint(new StubDriver()).Describe());
            var step = described["requestBody"]["content"]["application/json"]
                ["schema"]["properties"]["steps"]["items"]["properties"];

            var names = new List<string>();
            foreach (var field in (JObject)step)
            {
                names.Add(field.Key);
            }

            CollectionAssert.AreEquivalent(new[] { "key", "click", "move", "wait", "hold" }, names);
        }

        [Test]
        public void DescribesEveryFieldItReturns()
        {
            var endpoint = new InputEndpoint(new StubDriver(new InputResult
            {
                State = InputScript.Done,
                Steps = 4,
                StepsDelivered = 4,
                ElapsedMs = 1800,
                DurationMs = 1800,
                IsPlaying = true,
                GameView = new ViewSize { Width = 1920, Height = 1080 },
                Note = "Something worth saying.",
            }));

            var body = JObject.Parse(Encoding.UTF8.GetString(
                endpoint.Handle(Requests.Of("POST", "/input", "{}")).Body));
            var described = JObject.FromObject(endpoint.Describe())
                ["responses"]["200"]["content"]["application/json"]["schema"]["properties"];

            foreach (var field in body)
            {
                Assert.IsNotNull(described[field.Key], string.Format("'{0}' is returned but not described.", field.Key));
            }
        }

        [Test]
        public void SaysPlainlyThatOnlyOneBackendWorks()
        {
            var described = JObject.FromObject(new InputEndpoint(new StubDriver()).Describe());
            var prose = described["description"].Value<string>();

            // REQ-0001's not-in-scope list asks for this in so many words: a backend that half-works is
            // worse than one that says what it is.
            StringAssert.Contains("Input System", prose);
            StringAssert.Contains("legacy `Input` manager", prose);
            StringAssert.Contains("Play mode only", prose);
        }
    }
}
