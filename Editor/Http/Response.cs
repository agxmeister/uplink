using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace Agxmeister.Uplink.Http
{
    /// <summary>
    /// An outbound response: a status, headers and an already-encoded body. Keeping the body as bytes lets
    /// endpoints return something other than JSON — a PNG screenshot, say — without changing this type.
    /// </summary>
    public sealed class Response
    {
        public const string JsonContentType = "application/json";

        private static readonly byte[] NoBody = new byte[0];

        public Response(int status, string contentType, byte[] body)
        {
            Status = status;
            ContentType = contentType ?? JsonContentType;
            Body = body ?? NoBody;
            Headers = new Dictionary<string, string>();
        }

        public int Status { get; private set; }

        public string ContentType { get; private set; }

        public byte[] Body { get; private set; }

        public IDictionary<string, string> Headers { get; private set; }

        /// <summary>Adds a header and returns this response, so headers can be attached where it is built.</summary>
        public Response With(string header, string value)
        {
            Headers[header] = value;
            return this;
        }

        /// <summary>Serializes <paramref name="payload"/> as the JSON body of the response.</summary>
        public static Response Json(int status, object payload)
        {
            return Text(status, JsonContentType, JsonConvert.SerializeObject(payload, Formatting.Indented));
        }

        /// <summary>The one error shape the API produces, so every failure looks the same to a client.</summary>
        public static Response Error(int status, string message)
        {
            return Json(status, new Dictionary<string, object> { { "error", message } });
        }

        public static Response Text(int status, string contentType, string body)
        {
            return new Response(status, contentType, Encoding.UTF8.GetBytes(body ?? string.Empty));
        }

        public static Response Bytes(int status, string contentType, byte[] body)
        {
            if (body == null)
            {
                throw new ArgumentNullException("body");
            }
            return new Response(status, contentType, body);
        }
    }
}
