using System.Collections.Generic;
using System.Text;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Capture;
using Agxmeister.Uplink.Compilation;
using Agxmeister.Uplink.Console;
using Agxmeister.Uplink.Hierarchy;
using Agxmeister.Uplink.PlayMode;
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
            public CompileResult Poll()
            {
                return new CompileResult { State = CompileLog.Compiling };
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

        private static JObject Document()
        {
            var probe = new NoScenes();
            var endpoints = new List<IEndpoint>
            {
                new StatusEndpoint(new StubProbe(new EditorStatus())),
                new ConsoleEndpoint(new ConsoleBuffer()),
                new CompileEndpoint(new NoCompiler()),
                new PlayModeEndpoint(new PlayModeControl(new StoppedEditor())),
                new ScreenshotEndpoint(new NoCapture()),
                new SceneEndpoint(probe),
                new ObjectEndpoint(probe),
                new TestsEndpoint(new NoTests()),
            };

            var openApi = new OpenApiEndpoint(endpoints, "Uplink", "Test.", "0.2.0");
            endpoints.Add(openApi);

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
                    "/openapi.json",
                },
                served);
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

            Assert.AreEqual(9, names.Count);
        }

        [Test]
        public void TellsAModelWhatEachToolIsFor()
        {
            foreach (var path in ((JObject)Document()["paths"]).Properties())
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
