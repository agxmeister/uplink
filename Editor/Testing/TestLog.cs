using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Agxmeister.Uplink.Testing
{
    /// <summary>
    /// The test-run cycle, and the results gathered during it.
    ///
    /// PlayMode tests reload the domain partway through, and even an EditMode suite outruns any sensible
    /// request timeout, so this follows the same idle → running → done cycle as
    /// <see cref="Agxmeister.Uplink.Compilation.CompileLog"/>: an idle call starts a run, a call during one
    /// says so, and a call after one hands the outcome over and returns to idle.
    ///
    /// Deliberately free of any Unity dependency, so the cycle can be stepped through in tests;
    /// <see cref="UnityTestRunner"/> is what actually drives the Test Framework.
    /// </summary>
    public sealed class TestLog
    {
        public const string Running = "running";
        public const string Done = "done";
        public const string Idle = "idle";

        private readonly object gate = new object();

        private TestRunState state = new TestRunState { Phase = Idle };

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

        /// <summary>
        /// Moves the cycle on and reports where it now stands, telling the caller whether that means it has
        /// to go and start a run.
        /// </summary>
        public TestOutcomeReport Advance(TestRunOptions options, DateTime now)
        {
            lock (gate)
            {
                switch (state.Phase)
                {
                    case Idle:
                        state = new TestRunState
                        {
                            Phase = Running,
                            Mode = options.Mode,
                            StartedAt = now,
                            Results = new List<TestOutcome>(),
                        };
                        return new TestOutcomeReport { Run = Report(options.IncludePassed), ShouldStart = true };

                    case Done:
                        var finished = Report(options.IncludePassed);
                        state.Phase = Idle;
                        return new TestOutcomeReport { Run = finished, ShouldStart = false };

                    default:
                        return new TestOutcomeReport { Run = Report(options.IncludePassed), ShouldStart = false };
                }
            }
        }

        public void Add(TestOutcome outcome)
        {
            lock (gate)
            {
                if (state.Results == null)
                {
                    state.Results = new List<TestOutcome>();
                }
                state.Results.Add(outcome);
            }
        }

        public void Completed(DateTime now)
        {
            lock (gate)
            {
                state.Phase = Done;
                state.DurationMs = (long)(now - state.StartedAt).TotalMilliseconds;
            }
        }

        /// <summary>
        /// The run could not happen — scripts that will not compile, most often. Recorded rather than thrown
        /// because by the time it is known, the request that asked for the run is long gone.
        /// </summary>
        public void Failed(string message, DateTime now)
        {
            lock (gate)
            {
                state.Error = message;
                Completed(now);
            }
        }

        public TestRunState Capture()
        {
            lock (gate)
            {
                return state;
            }
        }

        public void Restore(TestRunState restored)
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
        private TestRun Report(bool includePassed)
        {
            var results = state.Results ?? new List<TestOutcome>();
            var summary = new TestSummary { Total = results.Count, DurationMs = state.DurationMs };
            var failures = new List<TestOutcome>();

            foreach (var result in results)
            {
                switch (result.Status)
                {
                    case TestState.Passed:
                        summary.Passed++;
                        break;
                    case TestState.Skipped:
                        summary.Skipped++;
                        break;
                    default:
                        summary.Failed++;
                        failures.Add(result);
                        break;
                }
            }

            return new TestRun
            {
                // `idle` is an internal resting place, never something a caller is told: a call that finds it
                // has already started the next run.
                State = state.Phase == Done ? Done : Running,
                Mode = state.Mode,
                Error = state.Error,
                Summary = summary,
                Failures = failures,
                Tests = includePassed ? new List<TestOutcome>(results) : null,
            };
        }
    }

    public sealed class TestOutcomeReport
    {
        public TestRun Run { get; set; }

        /// <summary>Whether the caller should now start a run.</summary>
        public bool ShouldStart { get; set; }
    }

    /// <summary>A run as stored between domain reloads.</summary>
    public sealed class TestRunState
    {
        [JsonProperty("phase")]
        public string Phase { get; set; }

        [JsonProperty("mode")]
        public string Mode { get; set; }

        [JsonProperty("error")]
        public string Error { get; set; }

        [JsonProperty("startedAt")]
        public DateTime StartedAt { get; set; }

        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }

        [JsonProperty("results")]
        public IList<TestOutcome> Results { get; set; }
    }
}
