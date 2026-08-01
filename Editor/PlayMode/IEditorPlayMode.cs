namespace Agxmeister.Uplink.PlayMode
{
    /// <summary>
    /// Play mode as the Editor sees it. A single-purpose abstraction that keeps the UnityEditor statics out of
    /// <see cref="PlayModeControl"/>, so the decision about what to do next can be tested without an Editor.
    /// </summary>
    public interface IEditorPlayMode
    {
        bool IsPlaying { get; }

        bool IsPaused { get; }

        /// <summary>
        /// Asks to enter play mode. Takes effect later: entering reloads the script domain, which would close
        /// the listener before the answer to the current request could be written.
        /// </summary>
        void Enter();

        /// <summary>Asks to leave play mode. Takes effect later, for the same reason as <see cref="Enter"/>.</summary>
        void Exit();

        /// <summary>Holds or resumes play mode. Takes effect at once.</summary>
        void Pause(bool paused);

        /// <summary>Advances a single frame. Takes effect at once.</summary>
        void Step();
    }
}
