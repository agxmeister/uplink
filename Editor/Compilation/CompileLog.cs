using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Agxmeister.Uplink.Compilation
{
    /// <summary>
    /// The compile cycle, and everything the compiler said during it.
    ///
    /// A compile reloads the domain, so the request that starts one can never be the request that reports it.
    /// The cycle is therefore idle → compiling → done, driven by repeated calls to the same endpoint: an idle
    /// call starts a run, a call during one says so, and a call after one hands the outcome over and returns
    /// to idle, so the next call unambiguously means "build again".
    ///
    /// Deliberately free of any Unity dependency, so the cycle can be stepped through in tests;
    /// <see cref="UnityCompiler"/> is what actually talks to the compiler.
    /// </summary>
    public sealed class CompileLog
    {
        public const string Compiling = "compiling";
        public const string Done = "done";
        public const string Idle = "idle";

        /// <summary>Enough of each to diagnose the build without answering with a whole log file.</summary>
        private const int MaxReported = 100;

        private readonly object gate = new object();

        private CompileState state = new CompileState { Phase = Idle };

        /// <summary>Where the cycle stands: <see cref="Idle"/>, <see cref="Compiling"/> or <see cref="Done"/>.</summary>
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

        /// <summary>Whether a run has been asked for but the compiler has not yet started one.</summary>
        public bool AwaitingStart
        {
            get
            {
                lock (gate)
                {
                    return state.AwaitingStart;
                }
            }
        }

        /// <summary>
        /// Moves the cycle on and reports where it now stands. The caller is told whether that means it has
        /// to go and ask the Editor to build — the decision belongs here, the doing does not.
        /// </summary>
        public CompileOutcome Advance(DateTime now)
        {
            lock (gate)
            {
                switch (state.Phase)
                {
                    case Idle:
                        // Messages are kept until a compile actually starts: if nothing needs rebuilding, the
                        // errors already standing are still the truth about this project.
                        state.Phase = Compiling;
                        state.AwaitingStart = true;
                        state.StartedAt = now;
                        state.Changed = false;
                        return new CompileOutcome { Result = Report(), ShouldTrigger = true };

                    case Done:
                        var finished = Report();
                        state.Phase = Idle;
                        return new CompileOutcome { Result = finished, ShouldTrigger = false };

                    default:
                        return new CompileOutcome { Result = Report(), ShouldTrigger = false };
                }
            }
        }

        /// <summary>The compiler has begun, so whatever it said last time no longer describes the project.</summary>
        public void Started(DateTime now)
        {
            lock (gate)
            {
                state.Messages = new List<CompileMessage>();
                state.AwaitingStart = false;
                state.Changed = true;
                state.StartedAt = now;
            }
        }

        public void Add(CompileMessage message)
        {
            lock (gate)
            {
                if (state.Messages == null)
                {
                    state.Messages = new List<CompileMessage>();
                }
                state.Messages.Add(message);
            }
        }

        public void Completed(DateTime now)
        {
            lock (gate)
            {
                state.Phase = Done;
                // Cleared here as well as in `Started`, so that a run which never started reports the
                // messages that were already standing rather than nothing at all.
                state.AwaitingStart = false;
                state.DurationMs = (long)(now - state.StartedAt).TotalMilliseconds;
            }
        }

        /// <summary>
        /// Whether a requested compile has waited long enough without starting to conclude that nothing
        /// needed rebuilding. <paramref name="busy"/> keeps the clock from running while the Editor is
        /// importing assets, which is a slow road to the same compile.
        /// </summary>
        public bool GaveUpWaiting(DateTime now, TimeSpan grace, bool busy)
        {
            lock (gate)
            {
                return state.AwaitingStart && !busy && now - state.StartedAt > grace;
            }
        }

        public CompileState Capture()
        {
            lock (gate)
            {
                return state;
            }
        }

        public void Restore(CompileState restored)
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
        private CompileResult Report()
        {
            var errors = new List<CompileMessage>();
            var warnings = new List<CompileMessage>();
            var errorCount = 0;
            var warningCount = 0;

            // While a requested compile has yet to start, the stored messages still belong to the previous
            // one, and reporting them against this run would be a lie. If it turns out nothing needed
            // rebuilding, `Started` never happens, `awaitingStart` is cleared by `Completed`, and they come
            // back as what they are: the errors already standing.
            if (state.Messages != null && !state.AwaitingStart)
            {
                foreach (var message in state.Messages)
                {
                    if (message.Level == CompileLevel.Error)
                    {
                        errorCount++;
                        if (errors.Count < MaxReported)
                        {
                            errors.Add(message);
                        }
                    }
                    else
                    {
                        warningCount++;
                        if (warnings.Count < MaxReported)
                        {
                            warnings.Add(message);
                        }
                    }
                }
            }

            return new CompileResult
            {
                // `idle` is an internal resting place, never something a caller is told: a call that finds it
                // has already started the next run.
                State = state.Phase == Done ? Done : Compiling,
                Changed = state.Changed,
                Errors = errors,
                Warnings = warnings,
                ErrorCount = errorCount,
                WarningCount = warningCount,
                DurationMs = state.Phase == Done ? state.DurationMs : 0,
            };
        }
    }

    public sealed class CompileOutcome
    {
        public CompileResult Result { get; set; }

        /// <summary>Whether the caller should now ask the Editor to build.</summary>
        public bool ShouldTrigger { get; set; }
    }

    /// <summary>A cycle as stored between domain reloads.</summary>
    public sealed class CompileState
    {
        [JsonProperty("phase")]
        public string Phase { get; set; }

        [JsonProperty("awaitingStart")]
        public bool AwaitingStart { get; set; }

        [JsonProperty("changed")]
        public bool Changed { get; set; }

        [JsonProperty("startedAt")]
        public DateTime StartedAt { get; set; }

        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }

        [JsonProperty("messages")]
        public IList<CompileMessage> Messages { get; set; }
    }

    public static class CompileLevel
    {
        public const string Error = "error";
        public const string Warning = "warning";
    }
}
