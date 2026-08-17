using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Http;
using Agxmeister.Uplink.Persistence;
using Agxmeister.Uplink.Services;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using ButtonControl = UnityEngine.InputSystem.Controls.ButtonControl;
using KeyControl = UnityEngine.InputSystem.Controls.KeyControl;

namespace Agxmeister.Uplink.Controls
{
    /// <summary>
    /// The one place that puts input into the running player loop.
    ///
    /// It is a service because a script outlives the request that asked for it: holds are measured in
    /// seconds, so the steps are handed to the player loop from `EditorApplication.update` over many frames
    /// while nobody is asking. And because play mode ending is a domain reload, the cycle's state goes to the
    /// session store as it changes rather than being gathered when a request finally comes back.
    ///
    /// Everything interesting about *when* a step happens lives in <see cref="InputScript"/>, which knows no
    /// Unity; this class only resolves names, delivers, and watches for play mode going away.
    /// </summary>
    public sealed class UnityInputDriver : IUplinkService, IInputDriver
    {
        private const string StateKey = "input";

        private readonly InputScript script;
        private readonly ISessionStore store;

        private bool attached;

        /// <summary>
        /// One state struct per device, carried between deliveries. A press sets a bit and a release clears
        /// it, and the whole state is queued each time: keyboard keys and mouse buttons are bit-addressed
        /// within their device's state, and `QueueDeltaStateEvent` refuses bit-addressed controls — so the
        /// shorter API fails on precisely the main use case.
        /// </summary>
        private KeyboardState keyboard;

        private MouseState mouse;

        public UnityInputDriver(InputScript script, ISessionStore store)
        {
            if (script == null)
            {
                throw new ArgumentNullException("script");
            }
            if (store == null)
            {
                throw new ArgumentNullException("store");
            }

            this.script = script;
            this.store = store;
        }

        public void Attach()
        {
            script.Restore(Stored.Read<ScriptState>(store, StateKey));

            // Being here at all means the domain just reloaded, and leaving play mode is one of the ways
            // that happens. A script still marked running with the Editor stopped ran into a closed door.
            if (script.Phase == InputScript.Running && !EditorApplication.isPlaying)
            {
                script.PlayModeEnded(Now);
                Persist();
            }

            EditorApplication.update += Tick;
            attached = true;
        }

        public void Detach()
        {
            if (attached)
            {
                EditorApplication.update -= Tick;
                attached = false;
            }

            Persist();
        }

        public InputResult Poll(IList<InputStep> steps)
        {
            ScriptPlan plan = null;

            if (steps != null)
            {
                if (!EditorApplication.isPlaying)
                {
                    throw new BadRequestException(
                        "Not in play mode. Input needs a running player loop to receive it — call " +
                        "set_play_mode with {\"target\": \"play\"} first, then send this again.");
                }
                plan = InputScript.Compile(steps, Resolved(steps));

                // Without this the whole feature does nothing whenever the Editor is not the foreground
                // application — which is exactly when an assistant is driving it. Measured: with the Editor
                // in the background `Time.frameCount` stays at 1, so the game never gets an Update in which
                // to read the key. Deliberately the runtime property and not the Player setting: it is
                // scoped to this play mode session and is gone when play mode ends, so nothing is written
                // to the project. See ADR-0016.
                Application.runInBackground = true;
            }

            var outcome = script.Advance(Now, plan);

            if (outcome.NothingToPlay)
            {
                throw new BadRequestException(
                    "No script is playing and none was given. Send {\"steps\": [...]} to play one, or use " +
                    "GET /input to look at the cycle without starting anything.");
            }

            Persist();
            return Decorated(outcome.Result);
        }

        public InputResult Peek()
        {
            // Nothing is persisted: the cycle was not touched, so what is stored still describes it.
            return Decorated(script.Observe(Now));
        }

        private static double Now
        {
            get { return EditorApplication.timeSinceStartup; }
        }

        /// <summary>
        /// Adds what the cycle cannot know: whether the Editor is playing, and the size of the surface a
        /// pointer step lands on. Outside play mode there is no Game view being drawn, so reporting a size
        /// would be reporting a guess.
        /// </summary>
        private static InputResult Decorated(InputResult result)
        {
            result.IsPlaying = EditorApplication.isPlaying;
            if (result.IsPlaying)
            {
                var size = GameViewSize;
                result.GameView = new ViewSize { Width = (int)size.x, Height = (int)size.y };
            }
            return result;
        }

        /// <summary>
        /// The size of the Game view, which is the space a pointer coordinate lives in.
        ///
        /// The camera is asked first, and neither of the two obvious answers is used, because both were
        /// measured lying. Inside the Editor `Screen.width`/`Screen.height` report the *view currently being
        /// processed*, and this runs from `EditorApplication.update`: with the Console focused they report
        /// the Console. `Display.main` turned out to report the same 2009x639 window rect rather than the
        /// Game view's 2560x1440 render target, so it is only a fallback. `Camera.main`'s pixel rect is the
        /// surface actually rendered, divided back out by the viewport in case the camera does not fill it.
        ///
        /// A pointer aimed with the wrong number lands nowhere near where the caller meant, and the caller —
        /// aiming from a screenshot — has no way to tell. Hence the belt and braces.
        /// </summary>
        private static Vector2 GameViewSize
        {
            get
            {
                var camera = Camera.main;
                if (camera != null && camera.pixelWidth > 1 && camera.pixelHeight > 1)
                {
                    var viewport = camera.rect;
                    var width = viewport.width > 0f ? camera.pixelWidth / viewport.width : camera.pixelWidth;
                    var height = viewport.height > 0f ? camera.pixelHeight / viewport.height : camera.pixelHeight;
                    return new Vector2(width, height);
                }

                var display = Display.main;
                if (display != null && display.renderingWidth > 1 && display.renderingHeight > 1)
                {
                    return new Vector2(display.renderingWidth, display.renderingHeight);
                }

                return new Vector2(Screen.width, Screen.height);
            }
        }

        private void Tick()
        {
            if (script.Phase != InputScript.Running)
            {
                return;
            }

            var now = Now;

            if (!EditorApplication.isPlaying)
            {
                // Caught here as well as in Attach, because leaving play mode does not always reload — and a
                // script quietly delivering into nothing would report success it did not earn.
                script.PlayModeEnded(now);
                Persist();
                return;
            }

            var due = script.Tick(now);
            foreach (var action in due)
            {
                Deliver(action);
            }

            if (due.Count > 0)
            {
                Persist();
            }

            if (script.IsFinished(now))
            {
                script.Completed(now);
                Persist();
            }
        }

        private void Deliver(ControlAction action)
        {
            if (action.Kind == ControlActionKind.Pointer)
            {
                var pointer = Mouse.current;
                if (pointer == null)
                {
                    return;
                }

                // The caller aims by looking at a screenshot, and `crop` counts from the top-left, so the
                // API does too. `Mouse.position` is bottom-left, hence the flip here rather than there.
                mouse.position = new Vector2(action.X, GameViewSize.y - action.Y);
                InputSystem.QueueStateEvent(pointer, mouse);
                return;
            }

            var control = InputSystem.FindControl(action.Path);

            var key = control as KeyControl;
            if (key != null)
            {
                var device = Keyboard.current;
                if (device == null)
                {
                    return;
                }
                keyboard.Set(key.keyCode, action.Pressed);
                InputSystem.QueueStateEvent(device, keyboard);
                return;
            }

            var button = control as ButtonControl;
            if (button != null && Mouse.current != null)
            {
                mouse = mouse.WithButton(ButtonOf(button.name), action.Pressed);
                InputSystem.QueueStateEvent(Mouse.current, mouse);
            }
        }

        /// <summary>
        /// Every control path a script names, resolved and checked now — during the request, where an unknown
        /// name can still be a `400` — rather than at delivery time, where nobody is listening.
        /// </summary>
        private static IList<string> Resolved(IList<InputStep> steps)
        {
            var paths = new List<string>();

            foreach (var step in steps)
            {
                if (step.Key != null)
                {
                    paths.Add(Verified(ControlPaths.OfKey(step.Key), step.Key, true));
                }
                else if (step.Click != null)
                {
                    paths.Add(Verified(ControlPaths.OfClick(step.Click), step.Click, false));
                }
                else
                {
                    // A move or a wait names no control, and the timeline keeps the slot so the two lists
                    // stay index-for-index with the steps.
                    paths.Add(null);
                }
            }

            return paths;
        }

        private static string Verified(string path, string asked, bool expectingKey)
        {
            var control = InputSystem.FindControl(path);
            if (control == null)
            {
                throw new BadRequestException(string.Format(
                    "The Input System has no control at '{0}' (from '{1}'). Give {2}.",
                    path, asked, ControlPaths.Accepted));
            }

            if (expectingKey && !(control is KeyControl))
            {
                throw new BadRequestException(string.Format(
                    "'{0}' is a control, but not a key. Use 'click' for mouse buttons.", asked));
            }
            if (!expectingKey && !(control is ButtonControl))
            {
                throw new BadRequestException(string.Format(
                    "'{0}' is a control, but not a button that can be clicked.", asked));
            }

            return path;
        }

        private static MouseButton ButtonOf(string name)
        {
            switch (name)
            {
                case "rightButton":
                    return MouseButton.Right;
                case "middleButton":
                    return MouseButton.Middle;
                case "forwardButton":
                    return MouseButton.Forward;
                case "backButton":
                    return MouseButton.Back;
                default:
                    return MouseButton.Left;
            }
        }

        private void Persist()
        {
            Stored.Write(store, StateKey, script.Capture());
        }
    }
}
