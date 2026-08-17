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

        /// <summary>A number within an accepted range, read the same way an integer is.</summary>
        public float Float(string name, float fallback, float minimum, float maximum)
        {
            var raw = String(name, null);
            if (raw == null)
            {
                return fallback;
            }

            return Number(name, raw, raw, minimum, maximum);
        }

        /// <summary>
        /// Three comma-separated numbers — a position or a direction — or null when the parameter is absent.
        /// Parsed in the invariant culture, so a client never has to know what the Editor's locale calls a
        /// decimal point.
        /// </summary>
        public float[] Triple(string name)
        {
            var raw = String(name, null);
            if (raw == null)
            {
                return null;
            }

            var parts = raw.Split(',');
            if (parts.Length != 3)
            {
                throw new BadRequestException(Malformed(name, "three numbers 'x,y,z'", raw));
            }

            var values = new float[3];
            for (var i = 0; i < 3; i++)
            {
                values[i] = Number(name, parts[i].Trim(), raw, float.MinValue, float.MaxValue);
            }

            return values;
        }

        /// <summary>
        /// Four comma-separated whole numbers — a rectangle — or null when the parameter is absent.
        /// <paramref name="shape"/> names the components in order, so the message can say which of them was
        /// out of range; <paramref name="minimums"/> carries the range rules component by component.
        /// </summary>
        public int[] Quad(string name, string shape, int[] minimums)
        {
            var raw = String(name, null);
            if (raw == null)
            {
                return null;
            }

            var components = shape.Split(',');
            var parts = raw.Split(',');
            if (parts.Length != 4)
            {
                throw new BadRequestException(Malformed(name, WholeNumbers(shape), raw));
            }

            var values = new int[4];
            for (var i = 0; i < 4; i++)
            {
                if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                {
                    throw new BadRequestException(Malformed(name, WholeNumbers(shape), raw));
                }
                if (values[i] < minimums[i])
                {
                    throw new BadRequestException(string.Format(
                        "'{0}' must be {1}, with '{2}' at least {3}, not '{4}'.",
                        name, WholeNumbers(shape), components[i], minimums[i], raw));
                }
            }

            return values;
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

        /// <summary>
        /// One number out of <paramref name="raw"/>, which is the whole value the client sent — a triple
        /// reports the triple it could not read, not just the component that failed.
        /// </summary>
        private static float Number(string name, string part, string raw, float minimum, float maximum)
        {
            float value;
            if (!float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                || float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new BadRequestException(string.Format(
                    "'{0}' must be a number, not '{1}'.", name, raw));
            }
            if (value < minimum || value > maximum)
            {
                throw new BadRequestException(string.Format(
                    "'{0}' must be between {1} and {2}, not {3}.",
                    name, minimum.ToString(CultureInfo.InvariantCulture),
                    maximum.ToString(CultureInfo.InvariantCulture), value.ToString(CultureInfo.InvariantCulture)));
            }

            return value;
        }

        private static string Malformed(string name, string shape, string raw)
        {
            return string.Format("'{0}' must be {1} separated by commas, not '{2}'.", name, shape, raw);
        }

        private static string WholeNumbers(string shape)
        {
            return string.Format("four whole numbers '{0}'", shape);
        }
    }
}
