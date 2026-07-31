using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Http;

namespace Agxmeister.Uplink.Api
{
    /// <summary>
    /// Dispatches a request to the endpoint that claims its method and path. Its only knowledge of the API is
    /// the endpoint collection it is handed.
    /// </summary>
    public sealed class Router : IRequestHandler
    {
        private readonly IEnumerable<IEndpoint> endpoints;

        public Router(IEnumerable<IEndpoint> endpoints)
        {
            if (endpoints == null)
            {
                throw new ArgumentNullException("endpoints");
            }
            this.endpoints = endpoints;
        }

        public Response Handle(Request request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            var allowed = new List<string>();
            foreach (var endpoint in endpoints)
            {
                if (!Route.Matches(endpoint.Path, request.Path))
                {
                    continue;
                }
                if (string.Equals(endpoint.Method, request.Method, StringComparison.Ordinal))
                {
                    return endpoint.Handle(request);
                }
                allowed.Add(endpoint.Method);
            }

            // The path is distinguished from the method so that a wrong verb never reads as a missing endpoint.
            if (allowed.Count == 0)
            {
                return Response.Error(404, string.Format("No endpoint at '{0}'.", request.Path));
            }

            var methods = string.Join(", ", allowed.ToArray());
            return Response
                .Error(405, string.Format("'{0}' accepts {1}, not {2}.", request.Path, methods, request.Method))
                .With("Allow", methods);
        }
    }
}
