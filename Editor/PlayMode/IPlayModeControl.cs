namespace Agxmeister.Uplink.PlayMode
{
    /// <summary>Puts the Editor into play mode, or takes it out, and says where it currently is.</summary>
    public interface IPlayModeControl
    {
        /// <summary>
        /// Asks for <paramref name="target"/> and reports where things stand. Called again with the same
        /// target it changes nothing, so a client can keep asking until the answer is `done`.
        /// </summary>
        PlayModeStatus Poll(string target);
    }
}
