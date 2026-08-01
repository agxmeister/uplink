using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Agxmeister.Uplink.Refresh
{
    /// <summary>
    /// The refresh cycle, and what the Editor re-read during it.
    ///
    /// Importing changed files can reload the domain, so — as with a compile — the request that starts a
    /// refresh cannot be the request that reports it. The cycle is idle → refreshing → done, driven by
    /// repeated calls to the same endpoint.
    ///
    /// Deliberately free of any Unity dependency, so the cycle can be stepped through in tests;
    /// <see cref="UnityRefresher"/> is what actually talks to the Editor.
    /// </summary>
    public sealed class RefreshLog
    {
        public const string Refreshing = "refreshing";
        public const string Done = "done";
        public const string Idle = "idle";

        private readonly object gate = new object();

        private RefreshState state = new RefreshState { Phase = Idle };

        /// <summary>Where the cycle stands: <see cref="Idle"/>, <see cref="Refreshing"/> or <see cref="Done"/>.</summary>
        public string Phase
        {
            get
            {
                lock (gate)
                {
                    return state.Phase;
                }
            }
        }

        /// <summary>Whether the run in flight was asked to re-open the scenes.</summary>
        public bool WantsScenes
        {
            get
            {
                lock (gate)
                {
                    return state.WantScenes;
                }
            }
        }

        /// <summary>
        /// Moves the cycle on and reports where it now stands. The caller is told whether that means it has
        /// to go and ask the Editor to re-read — the decision belongs here, the doing does not.
        /// </summary>
        public RefreshOutcome Advance(DateTime now, bool scenes)
        {
            lock (gate)
            {
                switch (state.Phase)
                {
                    case Idle:
                        state.Phase = Refreshing;
                        state.WantScenes = scenes;
                        state.StartedAt = now;
                        state.Reloaded = false;
                        state.OpenScenes = null;
                        return new RefreshOutcome { Result = Report(), ShouldTrigger = true };

                    case Done:
                        var finished = Report();
                        state.Phase = Idle;
                        return new RefreshOutcome { Result = finished, ShouldTrigger = false };

                    default:
                        return new RefreshOutcome { Result = Report(), ShouldTrigger = false };
                }
            }
        }

        /// <summary>The Editor has finished re-reading, and these are the scenes it now holds.</summary>
        public void Completed(DateTime now, bool reloaded, IList<OpenScene> scenes)
        {
            lock (gate)
            {
                state.Phase = Done;
                state.Reloaded = reloaded;
                state.OpenScenes = scenes;
                state.DurationMs = (long)(now - state.StartedAt).TotalMilliseconds;
            }
        }

        public RefreshState Capture()
        {
            lock (gate)
            {
                return state;
            }
        }

        public void Restore(RefreshState restored)
        {
            if (restored == null)
            {
                return;
            }

            lock (gate)
            {
                state = restored;
                if (string.IsNullOrEmpty(state.Phase))
                {
                    state.Phase = Idle;
                }
            }
        }

        /// <summary>Must be called under the lock.</summary>
        private RefreshResult Report()
        {
            return new RefreshResult
            {
                // `idle` is an internal resting place, never something a caller is told: a call that finds it
                // has already started the next run.
                State = state.Phase == Done ? Done : Refreshing,
                Reloaded = state.Reloaded,
                Scenes = state.OpenScenes ?? new List<OpenScene>(),
                DurationMs = state.Phase == Done ? state.DurationMs : 0,
            };
        }
    }

    public sealed class RefreshOutcome
    {
        public RefreshResult Result { get; set; }

        /// <summary>Whether the caller should now ask the Editor to re-read.</summary>
        public bool ShouldTrigger { get; set; }
    }

    /// <summary>A cycle as stored between domain reloads.</summary>
    public sealed class RefreshState
    {
        [JsonProperty("phase")]
        public string Phase { get; set; }

        /// <summary>What the run in flight was asked to do about scenes.</summary>
        [JsonProperty("wantScenes")]
        public bool WantScenes { get; set; }

        /// <summary>Whether the finished run actually re-opened them.</summary>
        [JsonProperty("reloaded")]
        public bool Reloaded { get; set; }

        [JsonProperty("startedAt")]
        public DateTime StartedAt { get; set; }

        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }

        /// <summary>The scenes as they stood when the run finished.</summary>
        [JsonProperty("openScenes")]
        public IList<OpenScene> OpenScenes { get; set; }
    }
}
