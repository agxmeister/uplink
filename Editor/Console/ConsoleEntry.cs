using Newtonsoft.Json;

namespace Agxmeister.Uplink.Console
{
    /// <summary>
    /// One message the Editor produced. Property names are the wire contract described by
    /// <see cref="ConsoleEndpoint.Describe"/>.
    /// </summary>
    public sealed class ConsoleEntry
    {
        /// <summary>
        /// Position in the session's message stream. Monotonic and never reused, so a client can say "only
        /// what happened after this" — see <see cref="ConsolePage.NextSince"/>.
        /// </summary>
        [JsonProperty("seq")]
        public long Seq { get; set; }

        /// <summary>
        /// When the message was logged, ISO-8601 in UTC. Absent on messages recovered from the Editor's own
        /// Console, which does not record it.
        /// </summary>
        [JsonProperty("time", NullValueHandling = NullValueHandling.Ignore)]
        public string Time { get; set; }

        /// <summary>One of `error`, `warning`, `log`.</summary>
        [JsonProperty("level")]
        public string Level { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        /// <summary>Omitted unless the client asked for it and the entry is an error.</summary>
        [JsonProperty("stackTrace", NullValueHandling = NullValueHandling.Ignore)]
        public string StackTrace { get; set; }
    }
}
