using System.Collections.Generic;
using System.Text;
using Agxmeister.Uplink.Status;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    [TestFixture]
    public sealed class StatusEndpointTests
    {
        private static readonly EditorStatus Sample = new EditorStatus
        {
            UplinkVersion = "0.1.0",
            UnityVersion = "2021.3.0f1",
            Platform = "OSXEditor",
            ProjectName = "Demo",
            ProjectPath = "/projects/demo/Assets",
            ActiveBuildTarget = "StandaloneOSX",
            ActiveScene = "Assets/Scenes/Main.unity",
            SceneDirty = true,
            DirtyScenes = new List<string> { "Assets/Scenes/Main.unity" },
            IsPlaying = true,
            IsPaused = false,
            IsCompiling = false,
            IsUpdating = false,
        };

        [Test]
        public void ServesTheProbedStatusAsJson()
        {
            var endpoint = new StatusEndpoint(new StubProbe(Sample));

            var response = endpoint.Handle(Requests.Of("GET", "/status"));
            var body = JObject.Parse(Encoding.UTF8.GetString(response.Body));

            Assert.AreEqual(200, response.Status);
            Assert.AreEqual("application/json", response.ContentType);
            Assert.AreEqual("2021.3.0f1", body["unityVersion"].Value<string>());
            Assert.AreEqual("Assets/Scenes/Main.unity", body["activeScene"].Value<string>());
            Assert.IsTrue(body["isPlaying"].Value<bool>());
        }

        [Test]
        public void DescribesEveryFieldItActuallyReturns()
        {
            var endpoint = new StatusEndpoint(new StubProbe(Sample));

            var body = JObject.Parse(Encoding.UTF8.GetString(endpoint.Handle(Requests.Of("GET", "/status")).Body));
            var described = JObject.FromObject(endpoint.Describe())
                ["responses"]["200"]["content"]["application/json"]["schema"]["properties"];

            foreach (var field in body)
            {
                Assert.IsNotNull(described[field.Key], string.Format("'{0}' is returned but not described.", field.Key));
            }
        }
    }
}
