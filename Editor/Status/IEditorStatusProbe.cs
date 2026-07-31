namespace Agxmeister.Uplink.Status
{
    /// <summary>
    /// Reads the current state of the Editor. A single-method abstraction that keeps the UnityEditor statics
    /// out of the endpoint, so the endpoint can be tested against a stand-in.
    /// </summary>
    public interface IEditorStatusProbe
    {
        /// <summary>Must be called on the Editor main thread.</summary>
        EditorStatus Read();
    }
}
