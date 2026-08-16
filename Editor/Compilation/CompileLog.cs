using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Console;
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
    /// `done` is not reported at the compiler's last word but after the domain reload a successful build
    /// causes, because the reload is where the interesting half of the outcome happens: it is what re-runs
    /// `[InitializeOnLoadMethod]` setup code, and what that code logged is handed over with the result. A run
    /// can also be *forced* — reloaded even when no script changed — which is what re-running such setup code
    /// deliberately looks like.
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

        /// <summary>Uplink's own messages announce the listener, which the response itself already proves.</summary>
        private const string OwnChatter = "[Uplink] ";

        private readonly object gate = new object();
        private readonly IConsoleReader console;

        private CompileState state = new CompileState { Phase = Idle };

        public CompileLog() : this(null)
        {
        }

        /// <summary>
        /// <paramref name="console"/> is where the messages of the reload are read from when a run finishes;
        /// without one, results simply carry no console page.
        /// </summary>
        public CompileLog(IConsoleReader console)
        {
            this.console = console;
        }

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

        /// <summary>Whether the run in flight was asked to reload even if nothing changed.</summary>
        public bool Forced
        {
            get
            {
                lock (gate)
                {
                    return state.Forced;
                }
            }
        }

        /// <summary>Whether the run has produced at least one error so far.</summary>
        public bool HasErrors
        {
            get
            {
                lock (gate)
                {
                    if (state.Messages == null)
                    {
                        return false;
                    }
                    foreach (var message in state.Messages)
                    {
                        if (message.Level == CompileLevel.Error)
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }
        }

        /// <summary>
        /// Whether the run in flight crossed the domain reload that just happened — its promised reload, or a
        /// mid-build one that cut it short. Meant to be asked on the far side of a reload, where being asked
        /// at all proves a reload took place; the answer says the run is ready to be closed out.
        /// </summary>
        public bool CrossedReload
        {
            get
            {
                lock (gate)
                {
                    return state.Phase == Compiling && (state.AwaitingReload || !state.AwaitingStart);
                }
            }
        }

        /// <summary>
        /// Moves the cycle on and reports where it now stands. The caller is told whether that means it has
        /// to go and ask the Editor to build — the decision belongs here, the doing does not.
        /// </summary>
        public CompileOutcome Advance(DateTime now)
        {
            return Advance(now, false);
        }

        /// <summary>
        /// As <see cref="Advance(DateTime)"/>; <paramref name="force"/> asks the run being started to reload
        /// the domain even when no script changed, which is how `[InitializeOnLoadMethod]` setup code is
        /// re-run without touching a file.
        /// </summary>
        public CompileOutcome Advance(DateTime now, bool force)
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
                        state.AwaitingReload = false;
                        state.StartedAt = now;
                        state.Changed = false;
                        state.Forced = force;
                        state.ReloadedWhilePlaying = false;
                        state.ConsoleSince = console == null ? 0 : console.Tail;
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

        /// <summary>
        /// A domain reload has been promised — by a successful build, or by an explicit request — and the run
        /// must not report done until it has happened and what it logged has been collected. Reporting done a
        /// little late only costs a poll; reporting it before the reload's messages exist misleads.
        /// </summary>
        public void ExpectReload(DateTime now)
        {
            lock (gate)
            {
                state.AwaitingReload = true;
                state.ReloadExpectedAt = now;
            }
        }

        /// <summary>
        /// The reload the run caused has completed, so the outcome — including everything the reload logged —
        /// can now be handed over. <paramref name="isPlaying"/> is remembered because play mode is what makes
        /// `[InitializeOnLoadMethod]` setup code silently do nothing, and the result should say so.
        /// </summary>
        public void Reloaded(DateTime now, bool isPlaying)
        {
            lock (gate)
            {
                state.Phase = Done;
                state.AwaitingStart = false;
                state.AwaitingReload = false;
                state.ReloadedWhilePlaying = isPlaying;
                state.DurationMs = (long)(now - state.StartedAt).TotalMilliseconds;
            }
        }

        /// <summary>Closes a run that will cause no reload: a failed build, or nothing to rebuild.</summary>
        public void Completed(DateTime now)
        {
            lock (gate)
            {
                state.Phase = Done;
                // Cleared here as well as in `Started`, so that a run which never started reports the
                // messages that were already standing rather than nothing at all.
                state.AwaitingStart = false;
                state.AwaitingReload = false;
                state.DurationMs = (long)(now - state.StartedAt).TotalMilliseconds;
            }
        }

        /// <summary>
        /// Whether a requested compile has waited long enough without starting to conclude that nothing
        /// needed rebuilding. <paramref name="busy"/> keeps the clock from running while the Editor is
        /// importing assets, which is a slow road to the same compile. A run whose reload is already promised
        /// is past this question.
        /// </summary>
        public bool GaveUpWaiting(DateTime now, TimeSpan grace, bool busy)
        {
            lock (gate)
            {
                return state.AwaitingStart && !state.AwaitingReload && !busy && now - state.StartedAt > grace;
            }
        }

        /// <summary>
        /// Whether a promised reload has failed to arrive for long enough to stop waiting — the escape hatch
        /// that keeps a reload the Editor never delivers from reading as `compiling` forever.
        /// </summary>
        public bool GaveUpOnReload(DateTime now, TimeSpan grace, bool busy)
        {
            lock (gate)
            {
                return state.Phase == Compiling && state.AwaitingReload && !busy
                    && now - state.ReloadExpectedAt > grace;
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

            var done = state.Phase == Done;

            return new CompileResult
            {
                // `idle` is an internal resting place, never something a caller is told: a call that finds it
                // has already started the next run.
                State = done ? Done : Compiling,
                Changed = state.Changed,
                Forced = state.Forced,
                Errors = errors,
                Warnings = warnings,
                ErrorCount = errorCount,
                WarningCount = warningCount,
                DurationMs = done ? state.DurationMs : 0,
                Console = done ? SinceTheRunBegan() : null,
                Note = done && state.ReloadedWhilePlaying
                    ? "This run's reload happened while play mode was on, where [InitializeOnLoadMethod] "
                        + "setup code silently does nothing. If such code was supposed to run, leave play "
                        + "mode and compile again with force: true."
                    : null,
            };
        }

        /// <summary>
        /// What the Editor logged between the run being asked for and now — the compile itself, and above all
        /// the reload it caused. Must be called under the lock; the console has its own and never calls back.
        /// </summary>
        private ConsolePage SinceTheRunBegan()
        {
            if (console == null)
            {
                return null;
            }

            var page = console.Read(new ConsoleQuery { Since = state.ConsoleSince, Limit = MaxReported });

            var entries = new List<ConsoleEntry>();
            foreach (var entry in page.Entries)
            {
                if (entry.Message != null && entry.Message.StartsWith(OwnChatter, StringComparison.Ordinal))
                {
                    Uncount(page.Counts, entry.Level);
                    continue;
                }
                entries.Add(entry);
            }
            page.Entries = entries;

            return page;
        }

        private static void Uncount(ConsoleCounts counts, string level)
        {
            if (counts == null)
            {
                return;
            }

            switch (level)
            {
                case ConsoleLevel.Error:
                    counts.Errors--;
                    break;
                case ConsoleLevel.Warning:
                    counts.Warnings--;
                    break;
                default:
                    counts.Logs--;
                    break;
            }
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

        /// <summary>A reload has been promised, and done must wait for it.</summary>
        [JsonProperty("awaitingReload")]
        public bool AwaitingReload { get; set; }

        [JsonProperty("changed")]
        public bool Changed { get; set; }

        /// <summary>The run was asked to reload even if nothing changed.</summary>
        [JsonProperty("forced")]
        public bool Forced { get; set; }

        /// <summary>Play mode was on when the run's reload completed, suppressing setup code.</summary>
        [JsonProperty("reloadedWhilePlaying")]
        public bool ReloadedWhilePlaying { get; set; }

        [JsonProperty("startedAt")]
        public DateTime StartedAt { get; set; }

        /// <summary>When the reload was promised, for the clock that stops waiting on one that never comes.</summary>
        [JsonProperty("reloadExpectedAt")]
        public DateTime ReloadExpectedAt { get; set; }

        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }

        /// <summary>Where the console stream stood when the run was asked for.</summary>
        [JsonProperty("consoleSince")]
        public long ConsoleSince { get; set; }

        [JsonProperty("messages")]
        public IList<CompileMessage> Messages { get; set; }
    }

    public static class CompileLevel
    {
        public const string Error = "error";
        public const string Warning = "warning";
    }
}
