namespace Agxmeister.Uplink.Hierarchy
{
    /// <summary>
    /// Reads the open scenes. Keeps the UnityEditor statics out of the endpoints, so both can be tested
    /// against a stand-in.
    /// </summary>
    public interface ISceneProbe
    {
        /// <summary>Must be called on the Editor main thread.</summary>
        SceneTree ReadTree(SceneQuery query);

        /// <summary>The object at <paramref name="path"/>, or null if there is nothing there.</summary>
        ObjectDetail ReadObject(string path);
    }

    public sealed class SceneQuery
    {
        public SceneQuery()
        {
            Depth = 3;
            Components = true;
        }

        /// <summary>Walk from this object instead of from the scene roots, or null for the whole hierarchy.</summary>
        public string Path { get; set; }

        /// <summary>How many generations below the starting point to include.</summary>
        public int Depth { get; set; }

        /// <summary>Whether to list each object's component types.</summary>
        public bool Components { get; set; }
    }
}
