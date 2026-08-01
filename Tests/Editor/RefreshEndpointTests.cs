using System;
using System.Collections.Generic;
using System.Text;
using Agxmeister.Uplink.Refresh;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    [TestFixture]
    public sealed class RefreshEndpointTests
    {
        [Test]
        public void StartingARefreshAnswers202()
        {
            var endpoint = new RefreshEndpoint(new StubRefresher());

            var response = endpoint.Handle(Requests.Of("POST", "/refresh"));
            var body = JObject.Parse(Encoding.UTF8.GetString(response.Body));

            Assert.AreEqual(202, response.Status);
            Assert.AreEqual(RefreshLog.Refreshing, body["state"].Value<string>());
        }

        [Test]
        public void HandsTheOutcomeOverWith200()
        {
            var refresher = new StubRefresher();
            var endpoint = new RefreshEndpoint(refresher);

            endpoint.Handle(Requests.Of("POST", "/refresh"));
            refresher.Finish();

            var response = endpoint.Handle(Requests.Of("POST", "/refresh"));
            var body = JObject.Parse(Encoding.UTF8.GetString(response.Body));

            Assert.AreEqual(200, response.Status);
            Assert.AreEqual(RefreshLog.Done, body["state"].Value<string>());
            Assert.IsTrue(body["reloaded"].Value<bool>());
        }

        [Test]
        public void RefusesToDiscardUnsavedWorkUnlessTold()
        {
            var refresher = new StubRefresher();
            refresher.Scenes[0].IsDirty = true;
            var endpoint = new RefreshEndpoint(refresher);

            var response = endpoint.Handle(Requests.Of("POST", "/refresh"));

            Assert.AreEqual(409, response.Status);
            Assert.IsFalse(refresher.Polled, "A refused call must not have started a refresh.");
            StringAssert.Contains("Assets/Scenes/Main.unity", Encoding.UTF8.GetString(response.Body));
        }

        [Test]
        public void DiscardsUnsavedWorkWhenTold()
        {
            var refresher = new StubRefresher();
            refresher.Scenes[0].IsDirty = true;
            var endpoint = new RefreshEndpoint(refresher);

            var response = endpoint.Handle(
                Requests.Of("POST", "/refresh", "{\"discardUnsavedChanges\":true}"));

            Assert.AreEqual(202, response.Status);
            Assert.IsTrue(refresher.Polled);
        }

        [Test]
        public void ImportingOnlyIsUnaffectedByUnsavedWork()
        {
            var refresher = new StubRefresher();
            refresher.Scenes[0].IsDirty = true;
            var endpoint = new RefreshEndpoint(refresher);

            var response = endpoint.Handle(Requests.Of("POST", "/refresh", "{\"scenes\":false}"));

            Assert.AreEqual(202, response.Status);
            Assert.IsFalse(refresher.WantedScenes, "scenes:false must not re-open anything.");
        }

        [Test]
        public void RefusesToRunDuringPlayMode()
        {
            var refresher = new StubRefresher { IsPlaying = true };
            var endpoint = new RefreshEndpoint(refresher);

            Assert.AreEqual(400, endpoint.Handle(Requests.Of("POST", "/refresh")).Status);
            Assert.IsFalse(refresher.Polled);
        }

        [Test]
        public void DescribesEveryFieldItActuallyReturns()
        {
            var refresher = new StubRefresher();
            var endpoint = new RefreshEndpoint(refresher);

            endpoint.Handle(Requests.Of("POST", "/refresh"));
            refresher.Finish();

            var body = JObject.Parse(
                Encoding.UTF8.GetString(endpoint.Handle(Requests.Of("POST", "/refresh")).Body));
            var described = JObject.FromObject(endpoint.Describe())
                ["responses"]["200"]["content"]["application/json"]["schema"]["properties"];

            foreach (var field in body)
            {
                Assert.IsNotNull(
                    described[field.Key], string.Format("'{0}' is returned but not described.", field.Key));
            }

            var scene = (JObject)body["scenes"][0];
            var describedScene = described["scenes"]["items"]["properties"];
            foreach (var field in scene)
            {
                Assert.IsNotNull(
                    describedScene[field.Key],
                    string.Format("scene field '{0}' is returned but not described.", field.Key));
            }
        }

        [Test]
        public void DescribesEveryFieldItAccepts()
        {
            var endpoint = new RefreshEndpoint(new StubRefresher());
            var described = JObject.FromObject(endpoint.Describe())
                ["requestBody"]["content"]["application/json"]["schema"]["properties"];

            Assert.IsNotNull(described["scenes"], "'scenes' is read but not described.");
            Assert.IsNotNull(
                described["discardUnsavedChanges"], "'discardUnsavedChanges' is read but not described.");
        }
    }

    /// <summary>Stands in for the Editor: records what it was asked, and finishes only when told to.</summary>
    internal sealed class StubRefresher : IRefresher
    {
        private readonly RefreshLog log = new RefreshLog();

        public IList<OpenScene> Scenes = new List<OpenScene>
        {
            new OpenScene
            {
                Name = "Main",
                Path = "Assets/Scenes/Main.unity",
                IsDirty = false,
                RootCount = 4,
            },
        };

        public bool IsPlaying { get; set; }

        public bool Polled { get; private set; }

        public bool WantedScenes { get; private set; }

        public IList<OpenScene> OpenScenes()
        {
            return Scenes;
        }

        public RefreshResult Poll(bool scenes)
        {
            Polled = true;
            WantedScenes = scenes;
            return log.Advance(DateTime.UtcNow, scenes).Result;
        }

        /// <summary>Stands in for the tick that does the work and records the outcome.</summary>
        public void Finish()
        {
            log.Completed(DateTime.UtcNow, WantedScenes, Scenes);
        }
    }
}
