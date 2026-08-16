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
            return Operation(
                operationId, summary, description,
                new Dictionary<string, object>
                {
                    { "200", JsonContent(responseDescription, responseSchema) },
                },
                null, null);
        }

        /// <summary>
        /// An Operation Object with whatever responses, query parameters and request body the endpoint has.
        /// <paramref name="responses"/> is keyed by status code, so an endpoint that answers `202` while it
        /// is still working describes that alongside its `200`.
        /// </summary>
        public static IDictionary<string, object> Operation(
            string operationId, string summary, string description,
            IDictionary<string, object> responses, IList<object> parameters,
            IDictionary<string, object> requestBody)
        {
            var operation = new Dictionary<string, object>
            {
                { "operationId", operationId },
                { "summary", summary },
                { "description", description },
                { "responses", responses },
            };

            // Omitted rather than empty, so an adapter reading the document sees no inputs at all.
            if (parameters != null && parameters.Count > 0)
            {
                operation["parameters"] = parameters;
            }
            if (requestBody != null)
            {
                operation["requestBody"] = requestBody;
            }

            return operation;
        }

        public static IDictionary<string, object> JsonContent(string description, object schema)
        {
            return Content(description, Response.JsonContentType, schema);
        }

        /// <summary>
        /// A failure response. There is one error shape in the API — see
        /// <see cref="Response.Error"/> — so there is one place that describes it.
        /// </summary>
        public static IDictionary<string, object> ErrorContent(string description)
        {
            return JsonContent(description, Object(new Dictionary<string, object>
            {
                { "error", Property("string", "What went wrong.") },
                { "status", Property("integer", "The HTTP status, repeated here in case the transport hides it.") },
                {
                    "retry",
                    Property(
                        "boolean",
                        "Present and true when the failure is transient — the Editor was busy or " +
                        "mid-reload — and the same call is worth making again.")
                },
            }));
        }

        /// <summary>A response that is not JSON — a PNG, say — described as an opaque binary body.</summary>
        public static IDictionary<string, object> BinaryContent(string description, string contentType)
        {
            return Content(description, contentType, new Dictionary<string, object>
            {
                { "type", "string" },
                { "format", "binary" },
            });
        }

        public static IDictionary<string, object> Content(string description, string contentType, object schema)
        {
            return Contents(description, new Dictionary<string, object> { { contentType, schema } });
        }

        /// <summary>
        /// A response the client can ask for in more than one form — a PNG or the same image as JSON, say —
        /// keyed by content type.
        /// </summary>
        public static IDictionary<string, object> Contents(
            string description, IDictionary<string, object> schemasByContentType)
        {
            var content = new Dictionary<string, object>();
            foreach (var pair in schemasByContentType)
            {
                content[pair.Key] = new Dictionary<string, object> { { "schema", pair.Value } };
            }

            return new Dictionary<string, object>
            {
                { "description", description },
                { "content", content },
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

        /// <summary>A property whose default is worth telling the client about.</summary>
        public static IDictionary<string, object> Property(string type, string description, object fallback)
        {
            var property = Property(type, description);
            property["default"] = fallback;
            return property;
        }

        /// <summary>A string property restricted to a fixed set of values.</summary>
        public static IDictionary<string, object> Choice(string description, string[] values, string fallback)
        {
            var property = Property("string", description);
            property["enum"] = values;
            if (fallback != null)
            {
                property["default"] = fallback;
            }
            return property;
        }

        public static IDictionary<string, object> Array(string description, object items)
        {
            return new Dictionary<string, object>
            {
                { "type", "array" },
                { "description", description },
                { "items", items },
            };
        }

        public static IDictionary<string, object> QueryParameter(
            string name, string description, object schema, bool required)
        {
            return new Dictionary<string, object>
            {
                { "name", name },
                { "in", "query" },
                { "description", description },
                { "required", required },
                { "schema", schema },
            };
        }

        public static IDictionary<string, object> JsonBody(string description, object schema, bool required)
        {
            return new Dictionary<string, object>
            {
                { "description", description },
                { "required", required },
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
    }
}
