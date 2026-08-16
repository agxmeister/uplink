using System.Collections.Generic;
using System.Text;
using Agxmeister.Uplink.Hierarchy;
using Agxmeister.Uplink.Http;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    [TestFixture]
    public sealed class HierarchyEndpointTests
    {
        private sealed class StubProbe : ISceneProbe
        {
            public SceneQuery Asked { get; private set; }

            public string AskedPath { get; private set; }

            public SceneTree ReadTree(SceneQuery query)
            {
                Asked = query;
                return new SceneTree
                {
                    Scenes = new List<SceneSummary>
                    {
                        new SceneSummary
                        {
                            Name = "Main",
                            Path = "Assets/Scenes/Main.unity",
                            IsLoaded = true,
                            IsActive = true,
                            Roots = new List<SceneNode>
                            {
                                new SceneNode
                                {
                                    Name = "Player",
                                    Path = "/Player",
                                    Active = true,
                                    Tag = "Player",
                                    Layer = "Default",
                                    Components = new List<string> { "Transform", "Rigidbody" },
                                    ChildCount = 0,
                                },
                            },
                        },
                    },
                };
            }

            public ObjectDetail ReadObject(string path)
            {
                AskedPath = path;
                if (path != "/Player")
                {
                    return null;
                }

                return new ObjectDetail
                {
                    Name = "Player",
                    Path = "/Player",
                    Scene = "Main",
                    Active = true,
                    Tag = "Player",
                    Layer = "Default",
                    Components = new List<ComponentDetail>
                    {
                        new ComponentDetail
                        {
                            Type = "Rigidbody",
                            Enabled = null,
                            Properties = new Dictionary<string, object> { { "m_Mass", 1.5f } },
                        },
                        new ComponentDetail
                        {
                            Type = "MeshRenderer",
                            Enabled = true,
                            Properties = new Dictionary<string, object>
                            {
                                { "m_Enabled", true },
                                { "m_CastShadows", 1 },
                            },
                        },
                    },
                    Children = new List<string>(),
                };
            }
        }

        [Test]
        public void WalksTheWholeHierarchyWhenAskedForNothingInParticular()
        {
            var probe = new StubProbe();

            new SceneEndpoint(probe).Handle(Requests.Of("GET", "/scene"));

            Assert.IsNull(probe.Asked.Path);
            Assert.AreEqual(3, probe.Asked.Depth);
            Assert.IsTrue(probe.Asked.Components);
        }

        [Test]
        public void PassesEveryParameterThrough()
        {
            var probe = new StubProbe();

            new SceneEndpoint(probe).Handle(Requests.Of("GET", "/scene", new Dictionary<string, string>
            {
                { "path", "/Level/Enemies" },
                { "depth", "1" },
                { "components", "false" },
            }));

            Assert.AreEqual("/Level/Enemies", probe.Asked.Path);
            Assert.AreEqual(1, probe.Asked.Depth);
            Assert.IsFalse(probe.Asked.Components);
        }

        [Test]
        public void RefusesADepthItWillNotWalk()
        {
            var endpoint = new SceneEndpoint(new StubProbe());

            Assert.Throws<BadRequestException>(
                () => endpoint.Handle(Requests.With("GET", "/scene", "depth", "500")));
        }

        [Test]
        public void SceneDescribesEveryFieldItActuallyReturns()
        {
            var endpoint = new SceneEndpoint(new StubProbe());

            var body = JObject.Parse(Encoding.UTF8.GetString(endpoint.Handle(Requests.Of("GET", "/scene")).Body));
            var described = JObject.FromObject(endpoint.Describe())
                ["responses"]["200"]["content"]["application/json"]["schema"]["properties"];

            foreach (var field in body)
            {
                Assert.IsNotNull(described[field.Key], string.Format("'{0}' is returned but not described.", field.Key));
            }

            var scene = described["scenes"]["items"]["properties"];
            foreach (var field in (JObject)body["scenes"][0])
            {
                Assert.IsNotNull(scene[field.Key], string.Format("scene '{0}' is returned but not described.", field.Key));
            }

            var node = scene["roots"]["items"]["properties"];
            foreach (var field in (JObject)body["scenes"][0]["roots"][0])
            {
                Assert.IsNotNull(node[field.Key], string.Format("object '{0}' is returned but not described.", field.Key));
            }
        }

        [Test]
        public void ReadsTheObjectAtTheGivenPath()
        {
            var probe = new StubProbe();

            var response = new ObjectEndpoint(probe).Handle(Requests.With("GET", "/object", "path", "/Player"));
            var body = JObject.Parse(Encoding.UTF8.GetString(response.Body));

            Assert.AreEqual(200, response.Status);
            Assert.AreEqual("/Player", probe.AskedPath);
            Assert.AreEqual("Rigidbody", body["components"][0]["type"].Value<string>());
            Assert.AreEqual(1.5f, body["components"][0]["properties"]["m_Mass"].Value<float>());
        }

        [Test]
        public void OmitsEnabledForAComponentThatCannotBeSwitchedOff()
        {
            var response = new ObjectEndpoint(new StubProbe())
                .Handle(Requests.With("GET", "/object", "path", "/Player"));
            var body = JObject.Parse(Encoding.UTF8.GetString(response.Body));

            Assert.IsNull(body["components"][0]["enabled"]);
        }

        [Test]
        public void ReportsAPathThatNamesNothingAsNotFound()
        {
            var response = new ObjectEndpoint(new StubProbe())
                .Handle(Requests.With("GET", "/object", "path", "/Nowhere"));
            var body = JObject.Parse(Encoding.UTF8.GetString(response.Body));

            Assert.AreEqual(404, response.Status);
            Assert.IsNotNull(body["error"]);
        }

        [Test]
        public void RequiresAPath()
        {
            var endpoint = new ObjectEndpoint(new StubProbe());

            Assert.Throws<BadRequestException>(() => endpoint.Handle(Requests.Of("GET", "/object")));
        }

        [Test]
        public void ObjectDescribesEveryFieldItActuallyReturns()
        {
            var endpoint = new ObjectEndpoint(new StubProbe());

            var body = JObject.Parse(Encoding.UTF8.GetString(
                endpoint.Handle(Requests.With("GET", "/object", "path", "/Player")).Body));
            var described = JObject.FromObject(endpoint.Describe())
                ["responses"]["200"]["content"]["application/json"]["schema"]["properties"];

            foreach (var field in body)
            {
                Assert.IsNotNull(described[field.Key], string.Format("'{0}' is returned but not described.", field.Key));
            }

            var component = described["components"]["items"]["properties"];
            foreach (var field in (JObject)body["components"][0])
            {
                Assert.IsNotNull(
                    component[field.Key], string.Format("component '{0}' is returned but not described.", field.Key));
            }
        }

        [Test]
        public void NarrowsToTheFieldsAskedAbout()
        {
            var response = new ObjectEndpoint(new StubProbe()).Handle(Requests.Of(
                "GET", "/object", new Dictionary<string, string>
                {
                    { "path", "/Player" },
                    { "fields", "m_Mass" },
                }));
            var body = JObject.Parse(Encoding.UTF8.GetString(response.Body));

            Assert.AreEqual(1, ((JArray)body["components"]).Count, "components with no matching field are noise");
            Assert.AreEqual("Rigidbody", body["components"][0]["type"].Value<string>());
            Assert.AreEqual(1.5f, body["components"][0]["properties"]["m_Mass"].Value<float>());
            Assert.IsNull(body["components"][0]["properties"]["m_Enabled"]);
        }

        [Test]
        public void NarrowsToTheComponentsAskedAbout()
        {
            var response = new ObjectEndpoint(new StubProbe()).Handle(Requests.Of(
                "GET", "/object", new Dictionary<string, string>
                {
                    { "path", "/Player" },
                    { "components", "meshrenderer" },
                }));
            var body = JObject.Parse(Encoding.UTF8.GetString(response.Body));

            Assert.AreEqual(1, ((JArray)body["components"]).Count);
            Assert.AreEqual(
                "MeshRenderer", body["components"][0]["type"].Value<string>(), "names match case-insensitively");
        }

        [Test]
        public void ANamedComponentAnswersEvenWhenNoFieldMatchesIt()
        {
            var response = new ObjectEndpoint(new StubProbe()).Handle(Requests.Of(
                "GET", "/object", new Dictionary<string, string>
                {
                    { "path", "/Player" },
                    { "components", "MeshRenderer" },
                    { "fields", "m_Mass" },
                }));
            var body = JObject.Parse(Encoding.UTF8.GetString(response.Body));

            Assert.AreEqual(1, ((JArray)body["components"]).Count);
            Assert.AreEqual(
                0, ((JObject)body["components"][0]["properties"]).Count,
                "empty properties say 'that field is not here', which is the answer");
        }

        [Test]
        public void AFilterThatMatchesNothingIsAnOrdinaryAnswer()
        {
            var response = new ObjectEndpoint(new StubProbe()).Handle(Requests.Of(
                "GET", "/object", new Dictionary<string, string>
                {
                    { "path", "/Player" },
                    { "fields", "m_NoSuchField" },
                }));

            Assert.AreEqual(200, response.Status);
            Assert.AreEqual(
                0,
                ((JArray)JObject.Parse(Encoding.UTF8.GetString(response.Body))["components"]).Count);
        }

        [Test]
        public void ObjectDescribesEveryParameterItAccepts()
        {
            var described = JObject.FromObject(new ObjectEndpoint(new StubProbe()).Describe());
            var names = new List<string>();
            foreach (var parameter in (JArray)described["parameters"])
            {
                names.Add(parameter["name"].Value<string>());
            }

            CollectionAssert.AreEquivalent(new[] { "path", "fields", "components" }, names);
        }

        [Test]
        public void SaysThatAPathIsRequired()
        {
            var described = JObject.FromObject(new ObjectEndpoint(new StubProbe()).Describe());

            Assert.IsTrue(described["parameters"][0]["required"].Value<bool>());
        }
    }
}
