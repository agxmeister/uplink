using System.Collections.Generic;

namespace Agxmeister.Uplink.Refresh
{
    /// <summary>
    /// Makes the Editor re-read what changed on disk. An unfocused Editor defers importing until someone
    /// clicks on it, so an API that writes project files needs a way to ask for it.
    /// </summary>
    public interface IRefresher
    {
        /// <summary>Whether play mode is running or about to, during which none of this is safe.</summary>
        bool IsPlaying { get; }

        /// <summary>
        /// The scenes the Editor has open, as they stand right now. Read before starting a run, so that a
        /// caller can be warned about unsaved work a reload would discard.
        /// </summary>
        IList<OpenScene> OpenScenes();

        /// <summary>Moves the refresh cycle on, starting one if none is running.</summary>
        RefreshResult Poll(bool scenes);
    }
}
