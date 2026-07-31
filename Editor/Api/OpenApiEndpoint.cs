using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Http;

namespace Agxmeister.Uplink.Api
{
    /// <summary>
    /// `GET /openapi.json`: the description of the API, assembled by asking every registered endpoint to
    /// describe itself. Because the spec is derived from the same collection the router dispatches on, the two
    /// cannot drift apart.
    /// </summary>
    public sealed class OpenApiEndpoint : IEndpoint
    {
        private readonly IEnumerable<IEndpoint> endpoints;
        private readonly string title;
        private readonly string description;
        private readonly string version;

        public OpenApiEndpoint(IEnumerable<IEndpoint> endpoints, string title, string description, string version)
        {
            if (endpoints == null)
            {
                throw new ArgumentNullException("endpoints");
            }

            this.endpoints = endpoints;
            this.title = title;
            this.description = description;
            this.version = version;
        }

        public string Method
        {
            get { return "GET"; }
        }

        public string Path
        {
            get { return "/openapi.json"; }
        }

        public IDictionary<string, object> Describe()
        {
            return Schema.Operation(
                "openapi",
                "Describe this API.",
                "Returns the OpenAPI 3.0 document for the endpoints this Editor instance serves.",
                Schema.Object(new Dictionary<string, object>()),
                "The OpenAPI document.");
        }

        public Response Handle(Request request)
        {
            return Response.Json(200, Document(request.Url.GetLeftPart(UriPartial.Authority)));
        }

        private IDictionary<string, object> Document(string serverUrl)
        {
            return new Dictionary<string, object>
            {
                { "openapi", "3.0.3" },
                {
                    "info", new Dictionary<string, object>
                    {
                        { "title", title },
                        { "description", description },
                        { "version", version },
                    }
                },
                {
                    "servers", new[]
                    {
                        new Dictionary<string, object> { { "url", serverUrl } },
                    }
                },
                { "paths", Paths() },
            };
        }

        private IDictionary<string, object> Paths()
        {
            var paths = new Dictionary<string, object>();
            foreach (var endpoint in endpoints)
            {
                var path = Route.Normalize(endpoint.Path);

                Dictionary<string, object> operations;
                object existing;
                if (paths.TryGetValue(path, out existing))
                {
                    operations = (Dictionary<string, object>)existing;
                }
                else
                {
                    operations = new Dictionary<string, object>();
                    paths[path] = operations;
                }

                operations[endpoint.Method.ToLowerInvariant()] = endpoint.Describe();
            }
            return paths;
        }
    }
}
