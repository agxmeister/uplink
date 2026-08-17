using System.Collections.Generic;
using System.Text;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Capture;
using Agxmeister.Uplink.Compilation;
using Agxmeister.Uplink.Console;
using Agxmeister.Uplink.Controls;
using Agxmeister.Uplink.Hierarchy;
using Agxmeister.Uplink.PlayMode;
using Agxmeister.Uplink.Refresh;
using Agxmeister.Uplink.Status;
using Agxmeister.Uplink.Testing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Agxmeister.Uplink.Tests
{
    /// <summary>
    /// The whole published API, assembled the way <see cref="Uplink"/> assembles it but with stand-ins for
    /// the Editor. What an adapter turns into tools is this document, so it is worth asserting on directly.
    /// </summary>
    [TestFixture]
    public sealed class ApiSurfaceTests
    {
        private sealed class NoCompiler : ICompiler
        {
            public CompileResult Poll(bool force)
            {
                return new CompileResult { State = CompileLog.Compiling };
            }

            public CompileResult Peek()
            {
                return new CompileResult { State = CompileLog.Idle };
            }
        }

        private sealed class NoTests : ITestRunner
        {
            public TestRun Poll(TestRunOptions options)
            {
                return new TestRun { State = TestLog.Running };
            }
        }

        private sealed class NoCapture : IViewCapture
        {
            public CaptureResult Take(CaptureRequest request)
            {
                return new CaptureResult { Png = new byte[0] };
            }
        }

        private sealed class NoInput : IInputDriver
        {
            public InputResult Poll(IList<InputStep> steps)
            {
                return new InputResult { State = InputScript.Running };
            }

            public InputResult Peek()
            {
                return new InputResult { State = InputScript.Idle };
            }
        }

        private sealed class NoScenes : ISceneProbe
        {
            public SceneTree ReadTree(SceneQuery query)
            {
                return new SceneTree();
            }

            public ObjectDetail ReadObject(string path)
            {
                return null;
            }
        }

        private sealed class StoppedEditor : IEditorPlayMode
        {
            public bool IsPlaying { get; private set; }

            public bool IsPaused { get; private set; }

            public void Enter()
            {
            }

            public void Exit()
            {
            }

            public void Pause(bool paused)
            {
            }

            public void Step()
            {
            }
        }

        /// <summary>
        /// The surface a project without the Input System gets — which is the ordinary case, and the one the
        /// optional assembly exists to keep whole.
        /// </summary>
        private static JObject Document()
        {
            return Document(false);
        }

        private static JObject Document(bool withInput)
        {
            var probe = new NoScenes();
            var endpoints = new EndpointRegistry();
            foreach (var endpoint in new List<IEndpoint>
            {
                new StatusEndpoint(new StubProbe(new EditorStatus())),
                new ConsoleEndpoint(new ConsoleBuffer()),
                new CompileEndpoint(new NoCompiler()),
                new CompileStatusEndpoint(new NoCompiler()),
                new PlayModeEndpoint(new PlayModeControl(new StoppedEditor())),
                new ScreenshotEndpoint(new NoCapture()),
                new SceneEndpoint(probe),
                new ObjectEndpoint(probe),
                new TestsEndpoint(new NoTests()),
                new RefreshEndpoint(new StubRefresher()),
            })
            {
                endpoints.Add(endpoint);
            }

            var openApi = new OpenApiEndpoint(endpoints, "Uplink", "Test.", "0.2.0");
            endpoints.Add(openApi);

            // Registered after the document endpoint, the way an optional capability really arrives: the
            // collection is read per request, so late is no different from early.
            if (withInput)
            {
                var driver = new NoInput();
                endpoints.Add(new InputEndpoint(driver));
                endpoints.Add(new InputStatusEndpoint(driver));
            }

            return JObject.Parse(Encoding.UTF8.GetString(
                openApi.Handle(Requests.Of("GET", "/openapi.json")).Body));
        }

        [Test]
        public void PublishesEveryTool()
        {
            var served = new List<string>();
            foreach (var path in ((JObject)Document()["paths"]).Properties())
            {
                served.Add(path.Name);
            }

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "/status", "/console", "/compile", "/play", "/screenshot", "/scene", "/object", "/tests",
                    "/refresh", "/openapi.json",
                },
                served);
        }

        [Test]
        public void IsAWholeApiWithoutTheOptionalInputTools()
        {
            var served = (JObject)Document()["paths"];

            // The point of the optional assembly: no Input System, no /input — and nothing else missing.
            Assert.IsNull(served["/input"]);
            Assert.IsNotNull(served["/screenshot"], "the rest of the API does not depend on it");
            Assert.IsNotNull(served["/openapi.json"]);
        }

        [Test]
        public void PublishesBothInputVerbsWhenTheBackendIsThere()
        {
            var input = (JObject)Document(true)["paths"]["/input"];

            Assert.IsNotNull(input, "a capability registered after the document endpoint still appears in it");
            Assert.IsNotNull(input["post"], "POST /input drives the cycle");
            Assert.IsNotNull(input["get"], "GET /input observes it, born with it per ADR-0012");
        }

        [Test]
        public void GivesEveryToolItsOwnNameWithTheInputToolsToo()
        {
            var names = new List<string>();
            foreach (var path in ((JObject)Document(true)["paths"]).Properties())
            {
                foreach (var operation in ((JObject)path.Value).Properties())
                {
                    var id = operation.Value["operationId"].Value<string>();
                    Assert.IsFalse(names.Contains(id), string.Format("'{0}' is used by two operations.", id));
                    names.Add(id);
                }
            }

            Assert.AreEqual(13, names.Count);
        }

        [Test]
        public void GivesEveryToolItsOwnName()
        {
            var names = new List<string>();
            foreach (var path in ((JObject)Document()["paths"]).Properties())
            {
                foreach (var operation in ((JObject)path.Value).Properties())
                {
                    var id = operation.Value["operationId"].Value<string>();
                    Assert.IsFalse(names.Contains(id), string.Format("'{0}' is used by two operations.", id));
                    names.Add(id);
                }
            }

            Assert.AreEqual(11, names.Count);
        }

        [Test]
        public void PublishesBothVerbsOfTheCompileCycleOnOnePath()
        {
            var compile = (JObject)Document()["paths"]["/compile"];

            Assert.IsNotNull(compile["post"], "POST /compile drives the cycle");
            Assert.IsNotNull(compile["get"], "GET /compile observes it, and the spec is derived, never edited");
        }

        [Test]
        public void TellsAModelWhatEachToolIsFor()
        {
            foreach (var path in ((JObject)Document(true)["paths"]).Properties())
            {
                foreach (var operation in ((JObject)path.Value).Properties())
                {
                    var described = operation.Value["description"].Value<string>();

                    Assert.IsNotNull(operation.Value["summary"], path.Name + " has no summary.");
                    Assert.IsTrue(
                        described != null && described.Length > 40,
                        string.Format("'{0}' needs a description a model can act on.", path.Name));
                }
            }
        }
    }
}
