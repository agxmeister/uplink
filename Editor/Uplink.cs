using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Configuration;
using Agxmeister.Uplink.Diagnostics;
using Agxmeister.Uplink.Http;
using Agxmeister.Uplink.Status;
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
        public const string Version = "0.1.0";

        private const string Title = "Uplink";
        private const string Description = "MCP remote control for the Unity Editor.";

        private static readonly TimeSpan MainThreadTimeout = TimeSpan.FromSeconds(10);

        private static readonly IUplinkSettings Settings = new EditorPrefsSettings();
        private static readonly IUplinkLog Log = new UnityLog();
        private static readonly MainThreadDispatcher Dispatcher = new MainThreadDispatcher();
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
            Endpoints.Add(OnMainThread(new StatusEndpoint(new UnityEditorStatusProbe(Version))));

            // Describes the collection above, so it is registered last; the list is read per request.
            Endpoints.Add(new OpenApiEndpoint(Endpoints, Title, Description, Version));

            Server = new HttpListenerServer(new FaultBarrier(new Router(Endpoints), Log), Log);

            EditorApplication.update += Dispatcher.Pump;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;

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
    }
}
