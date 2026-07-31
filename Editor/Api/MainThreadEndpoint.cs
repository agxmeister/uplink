using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Http;
using Agxmeister.Uplink.Threading;

namespace Agxmeister.Uplink.Api
{
    /// <summary>
    /// Decorates an endpoint that touches UnityEditor APIs, marshalling its execution onto the main thread.
    /// Endpoints therefore contain no threading code, and endpoints that need no Editor access — the OpenAPI
    /// document — pay nothing.
    /// </summary>
    public sealed class MainThreadEndpoint : IEndpoint
    {
        private readonly IEndpoint inner;
        private readonly IMainThreadDispatcher dispatcher;
        private readonly TimeSpan timeout;

        public MainThreadEndpoint(IEndpoint inner, IMainThreadDispatcher dispatcher, TimeSpan timeout)
        {
            if (inner == null)
            {
                throw new ArgumentNullException("inner");
            }
            if (dispatcher == null)
            {
                throw new ArgumentNullException("dispatcher");
            }

            this.inner = inner;
            this.dispatcher = dispatcher;
            this.timeout = timeout;
        }

        public string Method
        {
            get { return inner.Method; }
        }

        public string Path
        {
            get { return inner.Path; }
        }

        public IDictionary<string, object> Describe()
        {
            return inner.Describe();
        }

        public Response Handle(Request request)
        {
            return dispatcher.Run(() => inner.Handle(request), timeout);
        }
    }
}
