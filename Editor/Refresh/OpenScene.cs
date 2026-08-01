using Newtonsoft.Json;

namespace Agxmeister.Uplink.Refresh
{
    /// <summary>One scene the Editor has open, as the API reports it.</summary>
    public sealed class OpenScene
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        /// <summary>Whether the Editor holds unsaved edits, which a reload would throw away.</summary>
        [JsonProperty("isDirty")]
        public bool IsDirty { get; set; }

        [JsonProperty("rootCount")]
        public int RootCount { get; set; }
    }
}
