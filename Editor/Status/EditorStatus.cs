using Newtonsoft.Json;

namespace Agxmeister.Uplink.Status
{
    /// <summary>
    /// A snapshot of what the Editor is and what it is doing, as returned by `GET /status`. Property names are
    /// the wire contract described by <see cref="StatusEndpoint.Describe"/>.
    /// </summary>
    public sealed class EditorStatus
    {
        [JsonProperty("uplinkVersion")]
        public string UplinkVersion { get; set; }

        [JsonProperty("unityVersion")]
        public string UnityVersion { get; set; }

        [JsonProperty("platform")]
        public string Platform { get; set; }

        [JsonProperty("projectName")]
        public string ProjectName { get; set; }

        [JsonProperty("projectPath")]
        public string ProjectPath { get; set; }

        [JsonProperty("activeBuildTarget")]
        public string ActiveBuildTarget { get; set; }

        [JsonProperty("activeScene")]
        public string ActiveScene { get; set; }

        [JsonProperty("isPlaying")]
        public bool IsPlaying { get; set; }

        [JsonProperty("isPaused")]
        public bool IsPaused { get; set; }

        [JsonProperty("isCompiling")]
        public bool IsCompiling { get; set; }

        [JsonProperty("isUpdating")]
        public bool IsUpdating { get; set; }
    }
}
