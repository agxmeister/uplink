using System.Collections.Generic;
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

        /// <summary>
        /// Whether the active scene holds edits that have not reached its file. Play mode and screenshots run
        /// the in-memory scene, so work can look finished and still not be persisted.
        /// </summary>
        [JsonProperty("sceneDirty")]
        public bool SceneDirty { get; set; }

        /// <summary>Every open scene with unsaved changes, for projects with more than one open.</summary>
        [JsonProperty("dirtyScenes")]
        public IList<string> DirtyScenes { get; set; }

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
