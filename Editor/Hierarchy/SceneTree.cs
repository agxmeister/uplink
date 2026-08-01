using System.Collections.Generic;
using Newtonsoft.Json;

namespace Agxmeister.Uplink.Hierarchy
{
    /// <summary>
    /// What `GET /scene` answers with. Property names are the wire contract described by
    /// <see cref="SceneEndpoint.Describe"/>.
    /// </summary>
    public sealed class SceneTree
    {
        [JsonProperty("scenes")]
        public IList<SceneSummary> Scenes { get; set; }

        /// <summary>
        /// The walk stopped short — of the requested depth, or of the number of objects the endpoint will
        /// return. Ask about a subtree with `path` to see the rest.
        /// </summary>
        [JsonProperty("truncated")]
        public bool Truncated { get; set; }
    }

    public sealed class SceneSummary
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>Asset path of the scene, empty for one that has never been saved.</summary>
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("isLoaded")]
        public bool IsLoaded { get; set; }

        /// <summary>Whether this is the scene new objects are created in.</summary>
        [JsonProperty("isActive")]
        public bool IsActive { get; set; }

        [JsonProperty("roots")]
        public IList<SceneNode> Roots { get; set; }
    }

    /// <summary>One GameObject, and as much of what hangs off it as was asked for.</summary>
    public sealed class SceneNode
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>Slash-separated path from the scene root, as `read_object` takes.</summary>
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("active")]
        public bool Active { get; set; }

        [JsonProperty("tag")]
        public string Tag { get; set; }

        [JsonProperty("layer")]
        public string Layer { get; set; }

        /// <summary>Type names of the components on this object; absent when they were not asked for.</summary>
        [JsonProperty("components", NullValueHandling = NullValueHandling.Ignore)]
        public IList<string> Components { get; set; }

        /// <summary>How many children it has, which is not always how many are listed below.</summary>
        [JsonProperty("childCount")]
        public int ChildCount { get; set; }

        /// <summary>Absent at the depth the walk stopped at, even where <see cref="ChildCount"/> is not 0.</summary>
        [JsonProperty("children", NullValueHandling = NullValueHandling.Ignore)]
        public IList<SceneNode> Children { get; set; }
    }
}
