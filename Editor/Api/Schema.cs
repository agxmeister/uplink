using System.Collections.Generic;
using Agxmeister.Uplink.Http;

namespace Agxmeister.Uplink.Api
{
    /// <summary>
    /// Builders for the fragments of OpenAPI an endpoint needs to describe itself, so endpoints spell out
    /// their contract rather than nest raw dictionaries.
    /// </summary>
    public static class Schema
    {
        /// <summary>An Operation Object with a single JSON response.</summary>
        public static IDictionary<string, object> Operation(
            string operationId, string summary, string description, object responseSchema,
            string responseDescription)
        {
            return new Dictionary<string, object>
            {
                { "operationId", operationId },
                { "summary", summary },
                { "description", description },
                {
                    "responses", new Dictionary<string, object>
                    {
                        { "200", JsonContent(responseDescription, responseSchema) },
                    }
                },
            };
        }

        public static IDictionary<string, object> JsonContent(string description, object schema)
        {
            return new Dictionary<string, object>
            {
                { "description", description },
                {
                    "content", new Dictionary<string, object>
                    {
                        {
                            Response.JsonContentType, new Dictionary<string, object>
                            {
                                { "schema", schema },
                            }
                        },
                    }
                },
            };
        }

        public static IDictionary<string, object> Object(IDictionary<string, object> properties)
        {
            return new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", properties },
            };
        }

        public static IDictionary<string, object> Property(string type, string description)
        {
            return new Dictionary<string, object>
            {
                { "type", type },
                { "description", description },
            };
        }
    }
}
