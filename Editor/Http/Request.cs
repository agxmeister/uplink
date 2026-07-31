using System;
using System.Collections.Generic;

namespace Agxmeister.Uplink.Http
{
    /// <summary>
    /// An inbound request, decoupled from <see cref="System.Net.HttpListener"/> so that endpoints can be
    /// exercised without a socket.
    /// </summary>
    public sealed class Request
    {
        private static readonly IDictionary<string, string> NoQuery = new Dictionary<string, string>();

        public Request(string method, Uri url, IDictionary<string, string> query, string body)
        {
            if (method == null)
            {
                throw new ArgumentNullException("method");
            }
            if (url == null)
            {
                throw new ArgumentNullException("url");
            }

            Method = method.ToUpperInvariant();
            Url = url;
            Path = Route.Normalize(url.AbsolutePath);
            Query = query ?? NoQuery;
            Body = body ?? string.Empty;
        }

        /// <summary>The HTTP method, upper-cased.</summary>
        public string Method { get; private set; }

        /// <summary>The requested path, normalized the same way endpoint paths are.</summary>
        public string Path { get; private set; }

        /// <summary>The full request URL; used to advertise the server's own address in the OpenAPI document.</summary>
        public Uri Url { get; private set; }

        public IDictionary<string, string> Query { get; private set; }

        /// <summary>The request body as text, never null.</summary>
        public string Body { get; private set; }
    }
}
