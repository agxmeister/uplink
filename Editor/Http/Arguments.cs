using System;
using System.Globalization;
using Newtonsoft.Json;

namespace Agxmeister.Uplink.Http
{
    /// <summary>
    /// The single place request inputs are read and validated, the way <see cref="Route.Normalize"/> is the
    /// single place paths are. Endpoints therefore never parse a query string or a body themselves, and a
    /// malformed input reads the same — a `400` with a message naming the parameter — wherever it appears.
    /// </summary>
    public sealed class Arguments
    {
        private readonly Request request;

        public Arguments(Request request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }
            this.request = request;
        }

        public string String(string name, string fallback)
        {
            string value;
            return request.Query.TryGetValue(name, out value) && !string.IsNullOrEmpty(value) ? value : fallback;
        }

        /// <summary>An integer within an accepted range; anything else is the client's mistake, not a default.</summary>
        public int Int(string name, int fallback, int minimum, int maximum)
        {
            var raw = String(name, null);
            if (raw == null)
            {
                return fallback;
            }

            int value;
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                throw new BadRequestException(string.Format("'{0}' must be a whole number, not '{1}'.", name, raw));
            }
            if (value < minimum || value > maximum)
            {
                throw new BadRequestException(string.Format(
                    "'{0}' must be between {1} and {2}, not {3}.", name, minimum, maximum, value));
            }

            return value;
        }

        public long Long(string name, long fallback, long minimum)
        {
            var raw = String(name, null);
            if (raw == null)
            {
                return fallback;
            }

            long value;
            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                throw new BadRequestException(string.Format("'{0}' must be a whole number, not '{1}'.", name, raw));
            }
            if (value < minimum)
            {
                throw new BadRequestException(string.Format(
                    "'{0}' must be at least {1}, not {2}.", name, minimum, value));
            }

            return value;
        }

        public bool Bool(string name, bool fallback)
        {
            var raw = String(name, null);
            if (raw == null)
            {
                return fallback;
            }

            switch (raw.ToLowerInvariant())
            {
                case "true":
                case "1":
                case "yes":
                    return true;
                case "false":
                case "0":
                case "no":
                    return false;
                default:
                    throw new BadRequestException(string.Format(
                        "'{0}' must be true or false, not '{1}'.", name, raw));
            }
        }

        /// <summary>
        /// One of a fixed set of values, matched case-insensitively and returned in the canonical spelling the
        /// endpoint declared — so a handler can compare with <c>==</c> against what its schema advertises.
        /// </summary>
        public string Choice(string name, string fallback, string[] allowed)
        {
            var raw = String(name, null);
            if (raw == null)
            {
                return fallback;
            }

            foreach (var candidate in allowed)
            {
                if (string.Equals(candidate, raw, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            throw new BadRequestException(string.Format(
                "'{0}' must be one of {1}, not '{2}'.", name, string.Join(", ", allowed), raw));
        }

        /// <summary>
        /// The request body as <typeparamref name="T"/>. An absent body yields a default instance, so an
        /// endpoint whose options are all optional can be called with no body at all.
        /// </summary>
        public T Body<T>() where T : new()
        {
            if (string.IsNullOrEmpty(request.Body.Trim()))
            {
                return new T();
            }

            T value;
            try
            {
                value = JsonConvert.DeserializeObject<T>(request.Body);
            }
            catch (JsonException exception)
            {
                throw new BadRequestException(string.Format("The request body is not valid JSON: {0}", exception.Message));
            }

            return value == null ? new T() : value;
        }
    }
}
