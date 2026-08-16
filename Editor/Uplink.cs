using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Capture;
using Agxmeister.Uplink.Compilation;
using Agxmeister.Uplink.Configuration;
using Agxmeister.Uplink.Console;
using Agxmeister.Uplink.Diagnostics;
using Agxmeister.Uplink.Hierarchy;
using Agxmeister.Uplink.Http;
using Agxmeister.Uplink.Persistence;
using Agxmeister.Uplink.PlayMode;
using Agxmeister.Uplink.Refresh;
using Agxmeister.Uplink.Services;
using Agxmeister.Uplink.Status;
using Agxmeister.Uplink.Testing;
using Agxmeister.Uplink.Threading;
using UnityEditor;

namespace Agxmeister.Uplink
{
    /// <summary>
    /// The composition root: the only place that names concrete types and wires them together, and the entry
    /// point that brings the API up with the Editor and takes it down before a domain reload.
    ///
    /// To add an endpoint, write a class implementing <see cref="IEndpoint"/> and register it below — nothing
    /// else in the package changes.
    /// </summary>
    [InitializeOnLoad]
    public static class Uplink
    {
        public const string Version = "0.3.0";

        private const string Title = "Uplink";
        private const string Description = "MCP remote control for the Unity Editor.";

        private static readonly TimeSpan MainThreadTimeout = TimeSpan.FromSeconds(10);

        private static readonly IUplinkSettings Settings = new EditorPrefsSettings();
        private static readonly IUplinkLog Log = new UnityLog();
        private static readonly ISessionStore Store = new SessionStateStore();
        private static readonly MainThreadDispatcher Dispatcher = new MainThreadDispatcher();
        private static readonly List<IUplinkService> Services = new List<IUplinkService>();
        private static readonly List<IEndpoint> Endpoints = new List<IEndpoint>();
        private static readonly HttpListenerServer Server;

        public static int Port
        {
            get { return Settings.Port; }
            set { Settings.Port = value; }
        }

        public static string BaseUrl
        {
            get { return string.Format("http://localhost:{0}", Port); }
        }

        public static bool IsRunning
        {
            get { return Server.IsRunning; }
        }

        public static string LastError
        {
            get { return Server.LastError; }
        }

        static Uplink()
        {
            var console = new ConsoleBuffer();
            Services.Add(new ConsoleCollector(console, Store, new UnityConsoleHistory()));

            // The compile log reads the console so a finished run can hand over what its reload logged.
            var compiler = new UnityCompiler(new CompileLog(console), Store);
            Services.Add(compiler);

            var playMode = new UnityPlayMode();
            Services.Add(playMode);

            var tests = new UnityTestRunner(new TestLog(), Store);
            Services.Add(tests);

            var refresher = new UnityRefresher(new RefreshLog(), Store);
            Services.Add(refresher);

            // Services first: they must be listening to the Editor before any endpoint is asked about it.
            Attach(Services);

            Endpoints.Add(OnMainThread(new StatusEndpoint(new UnityEditorStatusProbe(Version))));
            // Reads its own buffer rather than the Editor, so it needs no main thread and no timeout.
            Endpoints.Add(new ConsoleEndpoint(console));
            Endpoints.Add(OnMainThread(new CompileEndpoint(compiler)));
            Endpoints.Add(OnMainThread(new PlayModeEndpoint(new PlayModeControl(playMode))));
            Endpoints.Add(OnMainThread(new ScreenshotEndpoint(new UnityViewCapture())));
            Endpoints.Add(OnMainThread(new RefreshEndpoint(refresher)));

            var scenes = new UnitySceneProbe();
            Endpoints.Add(OnMainThread(new SceneEndpoint(scenes)));
            Endpoints.Add(OnMainThread(new ObjectEndpoint(scenes)));

            Endpoints.Add(OnMainThread(new TestsEndpoint(tests)));

            // Describes the collection above, so it is registered last; the list is read per request.
            Endpoints.Add(new OpenApiEndpoint(Endpoints, Title, Description, Version));

            Server = new HttpListenerServer(new FaultBarrier(new Router(Endpoints), Log), Log);

            EditorApplication.update += Dispatcher.Pump;
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
            EditorApplication.quitting += Shutdown;

            Start();
        }

        public static void Start()
        {
            Server.Start(Port);
        }

        public static void Stop()
        {
            Server.Stop();
        }

        public static void Restart()
        {
            Stop();
            Start();
        }

        /// <summary>Wraps an endpoint that touches UnityEditor APIs, which are legal on the main thread only.</summary>
        private static IEndpoint OnMainThread(IEndpoint endpoint)
        {
            return new MainThreadEndpoint(endpoint, Dispatcher, MainThreadTimeout);
        }

        /// <summary>
        /// Registers a service and brings it up. A service that cannot attach must not take the API down with
        /// it: the Editor is still worth talking to without one collector.
        /// </summary>
        private static void Attach(ICollection<IUplinkService> services)
        {
            foreach (var service in services)
            {
                try
                {
                    service.Attach();
                }
                catch (Exception exception)
                {
                    Log.Error(string.Format("{0} could not start: {1}", service.GetType().Name, exception));
                }
            }
        }

        /// <summary>Everything the domain about to be discarded owns: the socket, and the services' state.</summary>
        private static void Shutdown()
        {
            Stop();

            foreach (var service in Services)
            {
                try
                {
                    service.Detach();
                }
                catch (Exception exception)
                {
                    Log.Error(string.Format("{0} could not stop cleanly: {1}", service.GetType().Name, exception));
                }
            }
        }
    }
}
