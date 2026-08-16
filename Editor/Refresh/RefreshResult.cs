using System.Collections.Generic;
using Newtonsoft.Json;

namespace Agxmeister.Uplink.Refresh
{
    /// <summary>What a refresh cycle reports.</summary>
    public sealed class RefreshResult
    {
        /// <summary><c>refreshing</c> while it runs, <c>done</c> when the outcome is being handed over.</summary>
        [JsonProperty("state")]
        public string State { get; set; }

        /// <summary>Whether the scenes were re-opened from their files, as opposed to only imported.</summary>
        [JsonProperty("reloaded")]
        public bool Reloaded { get; set; }

        [JsonProperty("scenes")]
        public IList<OpenScene> Scenes { get; set; }

        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }

        /// <summary>
        /// Whether the Editor is in play mode right now. Set by the service at answer time, not part of the
        /// stored cycle, because it describes the Editor rather than the run.
        /// </summary>
        [JsonProperty("isPlaying")]
        public bool IsPlaying { get; set; }
    }
}
