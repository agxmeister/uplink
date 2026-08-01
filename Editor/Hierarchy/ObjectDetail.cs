using System.Collections.Generic;
using Newtonsoft.Json;

namespace Agxmeister.Uplink.Hierarchy
{
    /// <summary>
    /// What `GET /object` answers with: one GameObject with its components' serialized values, which is how
    /// a client checks that an edit landed where it was meant to.
    /// </summary>
    public sealed class ObjectDetail
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("scene")]
        public string Scene { get; set; }

        [JsonProperty("active")]
        public bool Active { get; set; }

        [JsonProperty("tag")]
        public string Tag { get; set; }

        [JsonProperty("layer")]
        public string Layer { get; set; }

        [JsonProperty("components")]
        public IList<ComponentDetail> Components { get; set; }

        [JsonProperty("children")]
        public IList<string> Children { get; set; }
    }

    public sealed class ComponentDetail
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        /// <summary>Absent on components that cannot be switched off.</summary>
        [JsonProperty("enabled", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Enabled { get; set; }

        /// <summary>
        /// The component's serialized fields, as the Inspector shows them. Values it cannot express as JSON
        /// are reported as a description of their type rather than omitted.
        /// </summary>
        [JsonProperty("properties")]
        public IDictionary<string, object> Properties { get; set; }
    }
}
