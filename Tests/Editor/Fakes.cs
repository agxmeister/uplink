using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Diagnostics;
using Agxmeister.Uplink.Http;
using Agxmeister.Uplink.Status;
using Agxmeister.Uplink.Threading;

namespace Agxmeister.Uplink.Tests
{
    internal static class Requests
    {
        public static Request Of(string method, string path)
        {
            return new Request(method, new Uri("http://localhost:8787" + path), null, null);
        }
    }

    internal sealed class SilentLog : IUplinkLog
    {
        public void Info(string message)
        {
        }

        public void Warning(string message)
        {
        }

        public void Error(string message)
        {
        }
    }

    /// <summary>Runs work inline, standing in for the Editor main thread.</summary>
    internal sealed class InlineDispatcher : IMainThreadDispatcher
    {
        public T Run<T>(Func<T> work, TimeSpan timeout)
        {
            return work();
        }
    }

    internal sealed class StubProbe : IEditorStatusProbe
    {
        private readonly EditorStatus status;

        public StubProbe(EditorStatus status)
        {
            this.status = status;
        }

        public EditorStatus Read()
        {
            return status;
        }
    }

    internal sealed class StubEndpoint : IEndpoint
    {
        private readonly Func<Request, Response> handle;

        public StubEndpoint(string method, string path, Func<Request, Response> handle)
        {
            Method = method;
            Path = path;
            this.handle = handle;
        }

        public string Method { get; private set; }

        public string Path { get; private set; }

        public IDictionary<string, object> Describe()
        {
            return Schema.Operation("stub", "Stub.", "Stub.", Schema.Object(new Dictionary<string, object>()), "Ok.");
        }

        public Response Handle(Request request)
        {
            return handle(request);
        }
    }
}
