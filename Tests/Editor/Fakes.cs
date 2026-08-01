using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Diagnostics;
using Agxmeister.Uplink.Http;
using Agxmeister.Uplink.Persistence;
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

        /// <summary>A request carrying query parameters, as `?a=1&amp;b=2` would produce.</summary>
        public static Request Of(string method, string path, IDictionary<string, string> query)
        {
            return new Request(method, new Uri("http://localhost:8787" + path), query, null);
        }

        public static Request Of(string method, string path, string body)
        {
            return new Request(method, new Uri("http://localhost:8787" + path), null, body);
        }

        /// <summary>Shorthand for a single query parameter, which is most of what endpoint tests need.</summary>
        public static Request With(string method, string path, string name, string value)
        {
            return Of(method, path, new Dictionary<string, string> { { name, value } });
        }
    }

    /// <summary>Stands in for Unity's SessionState, which only exists inside a running Editor.</summary>
    internal sealed class InMemoryStore : ISessionStore
    {
        private readonly IDictionary<string, string> values = new Dictionary<string, string>();

        public string Get(string key)
        {
            string value;
            return values.TryGetValue(key, out value) ? value : null;
        }

        public void Set(string key, string value)
        {
            values[key] = value;
        }

        public void Remove(string key)
        {
            values.Remove(key);
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
        private readonly IDictionary<string, object> description;

        public StubEndpoint(string method, string path, Func<Request, Response> handle)
            : this(method, path, handle, null)
        {
        }

        public StubEndpoint(
            string method, string path, Func<Request, Response> handle, IDictionary<string, object> description)
        {
            Method = method;
            Path = path;
            this.handle = handle;
            this.description = description;
        }

        public string Method { get; private set; }

        public string Path { get; private set; }

        public IDictionary<string, object> Describe()
        {
            return description ?? Schema.Operation(
                "stub", "Stub.", "Stub.", Schema.Object(new Dictionary<string, object>()), "Ok.");
        }

        public Response Handle(Request request)
        {
            return handle(request);
        }
    }
}
