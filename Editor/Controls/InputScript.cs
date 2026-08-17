using System.Collections.Generic;
using Newtonsoft.Json;

namespace Agxmeister.Uplink.Controls
{
    /// <summary>
    /// The input cycle, and the schedule of presses and releases it plays.
    ///
    /// A script takes real time — a hold measured in seconds spans frames — so the request that starts one
    /// cannot be the request that reports it, and play mode ending mid-script is a domain reload besides.
    /// The cycle is therefore the <see cref="Compilation.CompileLog"/> shape: idle → running → done, driven
    /// by repeated calls to the same endpoint, with the result handed over exactly once.
    ///
    /// Beside the cycle sits the clock. <see cref="Tick"/> turns "what time is it" into "what is due", which
    /// is the only interesting logic here and the reason none of this knows what Unity is: a test constructs
    /// a script, calls Tick at three chosen moments and asserts on the actions, with no
    /// `EditorApplication.update` anywhere in its path.
    /// </summary>
    public sealed class InputScript
    {
        public const string Idle = "idle";
        public const string Running = "running";
        public const string Done = "done";

        /// <summary>
        /// What a step whose duration was not stated is worth. Long enough that a player loop running at any
        /// sane frame rate sees the press and the release on different frames, short enough to read as a tap.
        /// </summary>
        public const double DefaultHold = 0.05;

        private readonly object gate = new object();

        private ScriptState state = new ScriptState { Phase = Idle };

        /// <summary>Where the cycle stands: <see cref="Idle"/>, <see cref="Running"/> or <see cref="Done"/>.</summary>
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
        /// Whether every action has been handed out and the last step's time has passed. A script whose final
        /// step is a release still has to wait for that release to fall due, so this is not "no actions left".
        /// </summary>
        public bool IsFinished(double now)
        {
            lock (gate)
            {
                return state.Phase == Running && Elapsed(now) >= state.DurationSeconds
                    && state.Cursor >= Count(state.Actions);
            }
        }

        /// <summary>
        /// Moves the cycle on and reports where it now stands, telling the caller whether it must now go and
        /// start playing. <paramref name="steps"/> is read only when the cycle is idle — a script already
        /// running is never replaced, because replacing it would make the endpoint non-idempotent in exactly
        /// the way the cycle exists to avoid.
        /// </summary>
        public InputOutcome Advance(double now, ScriptPlan plan)
        {
            lock (gate)
            {
                switch (state.Phase)
                {
                    case Idle:
                        if (plan == null)
                        {
                            // Nothing running and nothing given. Starting the last script again would be a
                            // surprising thing for an empty call to do, so it is refused rather than guessed.
                            return new InputOutcome { Result = Report(now), NothingToPlay = true };
                        }

                        state = new ScriptState
                        {
                            Phase = Running,
                            Actions = plan.Actions,
                            StepEnds = plan.StepEnds,
                            DurationSeconds = plan.DurationSeconds,
                            Steps = plan.Steps,
                            StartedAt = now,
                            Cursor = 0,
                            PlayModeEnded = false,
                        };
                        return new InputOutcome { Result = Report(now), ShouldStart = true };

                    case Done:
                        var finished = Report(now);
                        state.Phase = Idle;
                        return new InputOutcome { Result = finished };

                    default:
                        var running = Report(now);
                        if (plan != null)
                        {
                            // Named rather than dropped: steps that vanished silently would look delivered.
                            running.Note = "A script is already running; these steps were not queued. Wait "
                                + "for 'state' to be 'done', take delivery of it, then send them again.";
                        }
                        return new InputOutcome { Result = running };
                }
            }
        }

        /// <summary>
        /// Reports where the cycle stands and changes nothing — the read half, `GET /input`. Like the compile
        /// cycle's observer it names <see cref="Idle"/>, which <see cref="Advance"/> never does, because to a
        /// caller that only looks the resting state is the answer to the question that matters.
        /// </summary>
        public InputResult Observe(double now)
        {
            lock (gate)
            {
                var result = Report(now);
                result.Stale = true;
                return result;
            }
        }

        /// <summary>
        /// The presses and releases that have fallen due since the last call. Returns an empty list rather
        /// than null when nothing is due, which is the common case — this is called every Editor frame.
        /// </summary>
        public IList<ControlAction> Tick(double now)
        {
            lock (gate)
            {
                var due = new List<ControlAction>();
                if (state.Phase != Running || state.Actions == null)
                {
                    return due;
                }

                var elapsed = Elapsed(now);
                while (state.Cursor < state.Actions.Count && state.Actions[state.Cursor].At <= elapsed)
                {
                    due.Add(state.Actions[state.Cursor]);
                    state.Cursor++;
                }

                return due;
            }
        }

        /// <summary>The script played to its end, and the outcome is now waiting to be handed over.</summary>
        public void Completed(double now)
        {
            lock (gate)
            {
                state.Phase = Done;
                state.ElapsedSeconds = Elapsed(now);
            }
        }

        /// <summary>
        /// Play mode ended before the script did. The run is closed as <see cref="Done"/> and says so, because
        /// a script that ran into a stopped Editor must not read as success.
        /// </summary>
        public void PlayModeEnded(double now)
        {
            lock (gate)
            {
                if (state.Phase != Running)
                {
                    return;
                }
                state.Phase = Done;
                state.PlayModeEnded = true;
                state.ElapsedSeconds = Elapsed(now);
            }
        }

        public ScriptState Capture()
        {
            lock (gate)
            {
                return state;
            }
        }

        public void Restore(ScriptState restored)
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

        /// <summary>
        /// Compiles steps into a timeline. Steps are sequential — the cursor advances by a step's hold or
        /// wait — because that is what a script reads as: tap, then hold left for 1.2s, then wait, then tap.
        /// A `move` is instantaneous, so a click aimed at where it put the pointer lands in the same frame.
        /// <paramref name="paths"/> supplies the already-resolved control path for each step, so this stays
        /// free of any knowledge of what a control is called.
        /// </summary>
        public static ScriptPlan Compile(IList<InputStep> steps, IList<string> paths)
        {
            var actions = new List<ControlAction>();
            var ends = new List<double>();
            var at = 0.0;

            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];

                if (step.Wait.HasValue)
                {
                    at += step.Wait.Value;
                }
                else if (step.Move != null)
                {
                    actions.Add(new ControlAction
                    {
                        At = at,
                        Kind = ControlActionKind.Pointer,
                        X = step.Move[0],
                        Y = step.Move[1],
                    });
                }
                else
                {
                    var hold = step.Hold ?? DefaultHold;
                    actions.Add(new ControlAction
                    {
                        At = at,
                        Kind = ControlActionKind.Button,
                        Path = paths[i],
                        Pressed = true,
                    });
                    at += hold;
                    actions.Add(new ControlAction
                    {
                        At = at,
                        Kind = ControlActionKind.Button,
                        Path = paths[i],
                        Pressed = false,
                    });
                }

                ends.Add(at);
            }

            return new ScriptPlan
            {
                Actions = actions,
                StepEnds = ends,
                DurationSeconds = at,
                Steps = steps.Count,
            };
        }

        /// <summary>Must be called under the lock.</summary>
        private InputResult Report(double now)
        {
            var running = state.Phase == Running;
            var elapsed = running ? Elapsed(now) : state.ElapsedSeconds;

            return new InputResult
            {
                State = state.Phase,
                Steps = state.Steps,
                StepsDelivered = Delivered(elapsed),
                ElapsedMs = Milliseconds(elapsed),
                DurationMs = Milliseconds(state.DurationSeconds),
                PlayModeEnded = state.PlayModeEnded,
            };
        }

        /// <summary>
        /// How many steps have run to their end. Counted from the clock rather than from actions handed out,
        /// so that a `wait` — which has no actions at all — still counts as delivered once it has elapsed.
        /// </summary>
        private int Delivered(double elapsed)
        {
            if (state.StepEnds == null)
            {
                return 0;
            }

            var delivered = 0;
            foreach (var end in state.StepEnds)
            {
                if (end <= elapsed)
                {
                    delivered++;
                }
            }
            return delivered;
        }

        private double Elapsed(double now)
        {
            var elapsed = now - state.StartedAt;
            return elapsed < 0 ? 0 : elapsed;
        }

        private static long Milliseconds(double seconds)
        {
            return (long)(seconds * 1000.0);
        }

        private static int Count(IList<ControlAction> actions)
        {
            return actions == null ? 0 : actions.Count;
        }
    }

    public sealed class InputOutcome
    {
        public InputResult Result { get; set; }

        /// <summary>Whether the caller should now go and start delivering the script.</summary>
        public bool ShouldStart { get; set; }

        /// <summary>
        /// Nothing was running and nothing was given, so there is no answer to hand back — the endpoint turns
        /// this into a `400` rather than silently replaying whatever ran last.
        /// </summary>
        public bool NothingToPlay { get; set; }
    }

    /// <summary>A compiled script: what to deliver, when, and how long the whole thing lasts.</summary>
    public sealed class ScriptPlan
    {
        public IList<ControlAction> Actions { get; set; }

        public IList<double> StepEnds { get; set; }

        public double DurationSeconds { get; set; }

        public int Steps { get; set; }
    }

    /// <summary>One press, release or pointer move, at a moment measured from the script's start.</summary>
    public sealed class ControlAction
    {
        [JsonProperty("at")]
        public double At { get; set; }

        [JsonProperty("kind")]
        public string Kind { get; set; }

        /// <summary>An Input System control path, already resolved — `&lt;Keyboard&gt;/space`.</summary>
        [JsonProperty("path", NullValueHandling = NullValueHandling.Ignore)]
        public string Path { get; set; }

        [JsonProperty("pressed")]
        public bool Pressed { get; set; }

        /// <summary>Pointer position in Game-view pixels from the top-left, for a pointer action.</summary>
        [JsonProperty("x")]
        public float X { get; set; }

        [JsonProperty("y")]
        public float Y { get; set; }
    }

    public static class ControlActionKind
    {
        public const string Button = "button";
        public const string Pointer = "pointer";
    }

    /// <summary>A cycle as stored between domain reloads — and play mode ending is one.</summary>
    public sealed class ScriptState
    {
        [JsonProperty("phase")]
        public string Phase { get; set; }

        [JsonProperty("actions")]
        public IList<ControlAction> Actions { get; set; }

        [JsonProperty("stepEnds")]
        public IList<double> StepEnds { get; set; }

        [JsonProperty("cursor")]
        public int Cursor { get; set; }

        [JsonProperty("steps")]
        public int Steps { get; set; }

        [JsonProperty("durationSeconds")]
        public double DurationSeconds { get; set; }

        [JsonProperty("elapsedSeconds")]
        public double ElapsedSeconds { get; set; }

        /// <summary>On the Editor's own clock, which survives a domain reload but not an Editor restart.</summary>
        [JsonProperty("startedAt")]
        public double StartedAt { get; set; }

        [JsonProperty("playModeEnded")]
        public bool PlayModeEnded { get; set; }
    }
}
