using System.Collections.Generic;
using System.Text;
using Agxmeister.Uplink.Controls;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    [TestFixture]
    public sealed class InputStatusEndpointTests
    {
        private static InputStatusEndpoint Watching(string state)
        {
            return new InputStatusEndpoint(new InputEndpointTests.StubDriver(new InputResult
            {
                State = state,
                Steps = 4,
                StepsDelivered = 2,
                Stale = true,
            }));
        }

        [Test]
        public void SharesItsPathWithTheVerbThatActs()
        {
            var reader = Watching(InputScript.Idle);
            var writer = new InputEndpoint(new InputEndpointTests.StubDriver());

            Assert.AreEqual(writer.Path, reader.Path);
            Assert.AreEqual("GET", reader.Method);
            Assert.AreEqual("POST", writer.Method);
        }

        [Test]
        public void AnswersAcceptedOnlyWhileAScriptIsPlaying()
        {
            Assert.AreEqual(202, Watching(InputScript.Running).Handle(Requests.Of("GET", "/input")).Status);
            Assert.AreEqual(200, Watching(InputScript.Done).Handle(Requests.Of("GET", "/input")).Status);
            Assert.AreEqual(200, Watching(InputScript.Idle).Handle(Requests.Of("GET", "/input")).Status);
        }

        [Test]
        public void NeverStartsAnything()
        {
            var driver = new InputEndpointTests.StubDriver();

            new InputStatusEndpoint(driver).Handle(Requests.Of("GET", "/input"));

            Assert.IsFalse(driver.WasAsked, "observing must go nowhere near the acting call");
        }

        [Test]
        public void DescribesTheRestingStateTheActingVerbNeverReports()
        {
            var described = JObject.FromObject(Watching(InputScript.Idle).Describe());
            var states = described["responses"]["200"]["content"]["application/json"]
                ["schema"]["properties"]["state"]["enum"];

            var named = new List<string>();
            foreach (var state in states)
            {
                named.Add(state.Value<string>());
            }

            CollectionAssert.AreEquivalent(
                new[] { InputScript.Idle, InputScript.Running, InputScript.Done }, named);
        }

        [Test]
        public void MarksWhatItReturnsAsNotHandedOver()
        {
            var endpoint = Watching(InputScript.Done);

            var body = JObject.Parse(Encoding.UTF8.GetString(
                endpoint.Handle(Requests.Of("GET", "/input")).Body));
            var described = JObject.FromObject(endpoint.Describe())
                ["responses"]["200"]["content"]["application/json"]["schema"]["properties"];

            Assert.IsTrue(body["stale"].Value<bool>());

            foreach (var field in body)
            {
                Assert.IsNotNull(described[field.Key], string.Format("'{0}' is returned but not described.", field.Key));
            }
        }
    }
}
