using Agxmeister.Uplink.Services;
using UnityEditor;

namespace Agxmeister.Uplink.PlayMode
{
    /// <summary>
    /// The one place that reads and writes the Editor's play mode.
    ///
    /// It is a service only so that it has an update tick to defer on: entering and leaving play mode reload
    /// the script domain, which would close the listener partway through answering the request that asked for
    /// it. Deferring by one tick is what lets the client be told the change has begun.
    /// </summary>
    public sealed class UnityPlayMode : IEditorPlayMode, IUplinkService
    {
        private bool? pending;

        public bool IsPlaying
        {
            get { return EditorApplication.isPlaying; }
        }

        public bool IsPaused
        {
            get { return EditorApplication.isPaused; }
        }

        public void Attach()
        {
            EditorApplication.update += Tick;
        }

        public void Detach()
        {
            EditorApplication.update -= Tick;
        }

        public void Enter()
        {
            pending = true;
        }

        public void Exit()
        {
            pending = false;
        }

        public void Pause(bool paused)
        {
            EditorApplication.isPaused = paused;
        }

        public void Step()
        {
            EditorApplication.Step();
        }

        private void Tick()
        {
            if (!pending.HasValue)
            {
                return;
            }

            var wanted = pending.Value;
            pending = null;
            EditorApplication.isPlaying = wanted;
        }
    }
}
