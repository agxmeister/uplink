using System.Collections.Generic;
using Agxmeister.Uplink.Http;

namespace Agxmeister.Uplink.Api
{
    /// <summary>
    /// One operation of the API. An endpoint owns its route, its own OpenAPI description and its behaviour,
    /// so adding a tool means adding a class and registering it — the router and the spec need no edits.
    /// </summary>
    public interface IEndpoint
    {
        /// <summary>The HTTP method this endpoint answers, upper-cased.</summary>
        string Method { get; }

        /// <summary>The path this endpoint answers, leading slash, no trailing slash.</summary>
        string Path { get; }

        /// <summary>
        /// This operation as an OpenAPI Operation Object. Its <c>operationId</c> becomes the tool name an
        /// MCP adapter exposes. Use <see cref="Schema"/> to build it.
        /// </summary>
        IDictionary<string, object> Describe();

        Response Handle(Request request);
    }
}
