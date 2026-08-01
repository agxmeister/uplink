using System;
using Agxmeister.Uplink.Http;

namespace Agxmeister.Uplink.PlayMode
{
    /// <summary>
    /// Decides what to do about a requested play mode, and whether the Editor has got there yet.
    ///
    /// Unlike compiling, this cycle needs nothing remembered across the domain reload that entering play mode
    /// causes: the Editor itself is the record of where it is. A call therefore only has to compare what was
    /// asked for with what is, and ask again if they differ.
    /// </summary>
    public sealed class PlayModeControl : IPlayModeControl
    {
        private readonly IEditorPlayMode editor;

        public PlayModeControl(IEditorPlayMode editor)
        {
            if (editor == null)
            {
                throw new ArgumentNullException("editor");
            }
            this.editor = editor;
        }

        public PlayModeStatus Poll(string target)
        {
            switch (target)
            {
                case PlayModeTarget.Play:
                    return Reach(target, editor.IsPlaying && !editor.IsPaused, Play);

                case PlayModeTarget.Stop:
                    return Reach(target, !editor.IsPlaying, editor.Exit);

                case PlayModeTarget.Pause:
                    RequirePlayMode(target);
                    return Reach(target, editor.IsPaused, () => editor.Pause(true));

                case PlayModeTarget.Step:
                    RequirePlayMode(target);
                    // A step has no state to arrive at — it is over as soon as it is taken.
                    editor.Step();
                    return Status(PlayModeCycle.Done, target);

                default:
                    throw new BadRequestException(string.Format(
                        "'{0}' is not something the Editor can be asked to do.", target));
            }
        }

        /// <summary>
        /// Reports `done` when the Editor is already where it was asked to be, and otherwise asks it to move
        /// and reports `changing`. Asking twice is harmless, which is what makes the call safe to repeat.
        /// </summary>
        private PlayModeStatus Reach(string target, bool arrived, Action move)
        {
            if (arrived)
            {
                return Status(PlayModeCycle.Done, target);
            }

            move();

            // Pausing and resuming take effect at once, so the second look can already say `done`; entering
            // and leaving cannot, and honestly report that they are under way.
            return Status(ArrivedAfter(target) ? PlayModeCycle.Done : PlayModeCycle.Changing, target);
        }

        private void Play()
        {
            if (editor.IsPlaying)
            {
                editor.Pause(false);
                return;
            }
            editor.Enter();
        }

        private bool ArrivedAfter(string target)
        {
            switch (target)
            {
                case PlayModeTarget.Play:
                    return editor.IsPlaying && !editor.IsPaused;
                case PlayModeTarget.Stop:
                    return !editor.IsPlaying;
                case PlayModeTarget.Pause:
                    return editor.IsPaused;
                default:
                    return true;
            }
        }

        private void RequirePlayMode(string target)
        {
            if (!editor.IsPlaying)
            {
                throw new BadRequestException(string.Format(
                    "'{0}' needs the Editor to be in play mode; ask for 'play' first.", target));
            }
        }

        private PlayModeStatus Status(string state, string target)
        {
            return new PlayModeStatus
            {
                State = state,
                Target = target,
                IsPlaying = editor.IsPlaying,
                IsPaused = editor.IsPaused,
            };
        }
    }
}
