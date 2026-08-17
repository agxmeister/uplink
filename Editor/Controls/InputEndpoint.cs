using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Http;

namespace Agxmeister.Uplink.Controls
{
    /// <summary>
    /// `POST /input`: plays a script of key, click and pointer steps into the running game, so states that
    /// only exist after somebody has played their way to them can be reached — and then photographed with
    /// `screenshot` or read with `read_scene`.
    /// </summary>
    public sealed class InputEndpoint : IEndpoint
    {
        /// <summary>
        /// Bounds a script so a typo cannot hold the cycle for an afternoon. Generous enough that no
        /// realistic script meets them, tight enough that a runaway one is caught at the door.
        /// </summary>
        public const int MaxSteps = 200;

        public const double MaxSeconds = 30.0;

        public const double MaxTotalSeconds = 300.0;

        private readonly IInputDriver driver;

        public InputEndpoint(IInputDriver driver)
        {
            if (driver == null)
            {
                throw new ArgumentNullException("driver");
            }
            this.driver = driver;
        }

        public string Method
        {
            get { return "POST"; }
        }

        public string Path
        {
            get { return "/input"; }
        }

        public IDictionary<string, object> Describe()
        {
            return Schema.Operation(
                "play_input",
                "Play keys, clicks and pointer moves into the running game.",
                "Drives the game through its own input, which is how to reach a state that only exists " +
                "after somebody has played their way to it — a menu two screens in, a rule that only shows " +
                "once a list is empty. Reach the state with this, then look at it with `screenshot` (try " +
                "`view=viewpoint`) or `read_scene`.\n\n" +
                "**Play mode only.** Outside it there is no player loop to receive events and the call " +
                "fails; call `set_play_mode` first. **The Input System package is the only supported " +
                "backend** — the legacy `Input` manager cannot be injected without the project cooperating, " +
                "so a project using it will not see this tool at all.\n\n" +
                "**Steps run one after another.** A step with `hold` holds that key or button down for that " +
                "many seconds and then moves on, so `[{\"key\":\"space\",\"hold\":0.05}, " +
                "{\"key\":\"leftArrow\",\"hold\":1.2}]` taps space and then holds left for 1.2 seconds. " +
                "`{\"wait\":0.5}` does nothing for half a second. `hold` defaults to 0.05, long enough for " +
                "the game to see a press and a release on different frames. A `{\"move\":[x,y]}` is " +
                "instantaneous, so a `{\"click\":\"left\"}` after it lands where it put the pointer.\n\n" +
                "**Control names are the Input System's own paths** — `<Keyboard>/space`, " +
                "`<Mouse>/leftButton` — and the project's `.inputactions` already speaks that language. The " +
                "short forms `space`, `leftArrow`, `a`, `left`, `right`, `middle` are accepted as sugar for " +
                "the common ones. A name the Input System does not know fails the call and says so, rather " +
                "than being delivered to nothing.\n\n" +
                "**Pointer coordinates count pixels from the top-left**, the same way `crop` does on " +
                "`screenshot`, not the bottom-left way `Mouse.position` does — the flip is done here. Every " +
                "response reports `gameView`, the size of the surface those coordinates land on, because a " +
                "pointer is useless without it.\n\n" +
                "**A script takes real time, so this is a repeated call, not one that waits.** The first " +
                "call starts the script and answers `202`; further calls report progress " +
                "(`stepsDelivered` of `steps`) and answer `202`; once it has played out, one call returns " +
                "`state: \"done\"` with `200` and hands the outcome over — the call after that would start " +
                "something new, so poll with `GET /input`, which reports the same states and never acts. " +
                "Steps sent while a script is playing are **not** queued and the response says so in " +
                "`note`. Watch `playModeEnded`: a game can quit mid-script, and a script that ran into a " +
                "stopped Editor does not read as success. (Send a body: a POST with no `Content-Length` at " +
                "all is rejected with `411` before this endpoint sees it.)\n\n" +
                "Starting a script turns on `Application.runInBackground`, because a Unity player does not " +
                "tick at all while the Editor is not the foreground application — without it the game would " +
                "never get an update in which to read the keys, and nothing would happen. That is the " +
                "runtime property, not the Player setting: it writes nothing to the project and it is gone " +
                "when play mode ends. It does mean the game keeps running in the background afterwards, " +
                "which is what makes a following `screenshot` show a live frame rather than a frozen one.",
                new Dictionary<string, object>
                {
                    {
                        "200", Schema.JsonContent(
                            "The script has played out; this is its outcome, handed over once.",
                            ResultSchema(false))
                    },
                    {
                        "202", Schema.JsonContent(
                            "A script has begun or is playing. Call again for the outcome.", ResultSchema(false))
                    },
                    {
                        "400", Schema.ErrorContent(
                            "Not in play mode, a control name the Input System does not know, a malformed " +
                            "step, or nothing to play.")
                    },
                    { "504", Schema.ErrorContent("The Editor was too busy to answer. Retry.") },
                },
                null,
                Schema.JsonBody(
                    "The script to play; only read when this call starts one.",
                    Schema.Object(new Dictionary<string, object>
                    {
                        { "steps", Schema.Array("The steps, played in order.", StepSchema()) },
                    }),
                    false));
        }

        public Response Handle(Request request)
        {
            var wanted = new Arguments(request).Body<InputRequest>();
            var result = driver.Poll(Checked(wanted.Steps));

            // 202 rather than 200 while it plays, so a client can tell "not finished" from "finished"
            // without reading the body.
            return Response.Json(result.State == InputScript.Done ? 200 : 202, result);
        }

        /// <summary>
        /// The shape of an input result, shared with <see cref="InputStatusEndpoint"/> so the two calls on
        /// `/input` cannot describe the same payload differently.
        /// </summary>
        public static IDictionary<string, object> ResultSchema(bool observed)
        {
            var properties = new Dictionary<string, object>
            {
                {
                    "state",
                    observed
                        ? Schema.Choice(
                            "Where the cycle stands: `running` while a script plays, `done` when an outcome " +
                            "is waiting for a POST to take delivery of it, `idle` when neither.",
                            new[] { InputScript.Idle, InputScript.Running, InputScript.Done },
                            null)
                        : Schema.Choice(
                            "Whether the script is still playing.",
                            new[] { InputScript.Running, InputScript.Done },
                            null)
                },
                { "steps", Schema.Property("integer", "How many steps the script has.") },
                {
                    "stepsDelivered",
                    Schema.Property("integer", "How many of them have run to their end so far.")
                },
                { "elapsedMs", Schema.Property("integer", "How long the script has been playing.") },
                { "durationMs", Schema.Property("integer", "How long the whole script lasts.") },
                {
                    "playModeEnded",
                    Schema.Property(
                        "boolean",
                        "True when play mode stopped before the script did. The steps after that point were " +
                        "never delivered, so a run with this set is not a success.")
                },
                { "isPlaying", Schema.Property("boolean", "Whether the Editor is in play mode right now.") },
                {
                    "gameView", Schema.Object(new Dictionary<string, object>
                    {
                        { "width", Schema.Property("integer", "Game view width in pixels.") },
                        { "height", Schema.Property("integer", "Game view height in pixels.") },
                    })
                },
                {
                    "note",
                    Schema.Property("string", "A warning the numbers would hide, present only when it applies.")
                },
            };

            if (observed)
            {
                properties["stale"] = Schema.Property(
                    "boolean",
                    "Present and true when this result was looked at rather than handed over, which every " +
                    "GET is. The same result can be read again, and while `state` is `done` a POST can " +
                    "still take delivery of it.");
            }

            return Schema.Object(properties);
        }

        private static IDictionary<string, object> StepSchema()
        {
            return Schema.Object(new Dictionary<string, object>
            {
                {
                    "key",
                    Schema.Property(
                        "string",
                        "A key to press: '<Keyboard>/space', or a short form such as 'space' or 'leftArrow'.")
                },
                {
                    "click",
                    Schema.Property(
                        "string",
                        "A mouse button to press: 'left', 'right', 'middle', or '<Mouse>/leftButton'.")
                },
                {
                    "move",
                    Schema.Array(
                        "Where to put the pointer, as [x, y] in Game-view pixels from the top-left.",
                        Schema.Property("number", "A pixel coordinate."))
                },
                { "wait", Schema.Property("number", "Seconds to do nothing for.") },
                {
                    "hold",
                    Schema.Property(
                        "number",
                        "Seconds to hold a 'key' or 'click' down before releasing it.",
                        InputScript.DefaultHold)
                },
            });
        }

        /// <summary>
        /// The steps as given, once they are known to make sense — or null when none were given at all, which
        /// is an ordinary poll rather than a mistake. Shape is settled here, where it is testable without an
        /// Editor; whether a control name exists is the driver's question, because only it can ask.
        /// </summary>
        private static IList<InputStep> Checked(IList<InputStep> steps)
        {
            if (steps == null || steps.Count == 0)
            {
                return null;
            }

            if (steps.Count > MaxSteps)
            {
                throw new BadRequestException(string.Format(
                    "A script may have at most {0} steps, not {1}.", MaxSteps, steps.Count));
            }

            var total = 0.0;
            for (var i = 0; i < steps.Count; i++)
            {
                total += Checked(steps[i], i);
            }

            if (total > MaxTotalSeconds)
            {
                throw new BadRequestException(string.Format(
                    "The whole script would take {0:0.##} seconds, and {1} is the most allowed. Split it, " +
                    "and look at what happened in between.", total, MaxTotalSeconds));
            }

            return steps;
        }

        /// <summary>How long one step lasts, having checked that it says exactly one thing.</summary>
        private static double Checked(InputStep step, int index)
        {
            if (step == null)
            {
                throw new BadRequestException(string.Format("Step {0} is empty.", index));
            }

            var kinds = 0;
            if (step.Key != null)
            {
                kinds++;
            }
            if (step.Click != null)
            {
                kinds++;
            }
            if (step.Move != null)
            {
                kinds++;
            }
            if (step.Wait.HasValue)
            {
                kinds++;
            }

            if (kinds != 1)
            {
                throw new BadRequestException(string.Format(
                    "Step {0} must say exactly one of 'key', 'click', 'move' or 'wait'; it says {1}.",
                    index, kinds));
            }

            if (step.Move != null)
            {
                if (step.Move.Length != 2)
                {
                    throw new BadRequestException(string.Format(
                        "Step {0}'s 'move' must be [x, y] in Game-view pixels from the top-left, not {1} " +
                        "numbers.", index, step.Move.Length));
                }
                if (step.Move[0] < 0f || step.Move[1] < 0f)
                {
                    throw new BadRequestException(string.Format(
                        "Step {0}'s 'move' must be inside the Game view, so neither coordinate can be " +
                        "negative.", index));
                }
            }

            if (step.Hold.HasValue)
            {
                if (step.Key == null && step.Click == null)
                {
                    throw new BadRequestException(string.Format(
                        "Step {0} has a 'hold' but nothing to hold; 'hold' goes with 'key' or 'click'. To " +
                        "let time pass, use 'wait'.", index));
                }
                Within(step.Hold.Value, "hold", index);
            }

            if (step.Wait.HasValue)
            {
                Within(step.Wait.Value, "wait", index);
                return step.Wait.Value;
            }

            if (step.Move != null)
            {
                return 0.0;
            }

            return step.Hold ?? InputScript.DefaultHold;
        }

        private static void Within(double seconds, string name, int index)
        {
            if (seconds < 0.0 || seconds > MaxSeconds || double.IsNaN(seconds))
            {
                throw new BadRequestException(string.Format(
                    "Step {0}'s '{1}' must be between 0 and {2} seconds, not {3}.",
                    index, name, MaxSeconds, seconds));
            }
        }
    }
}
