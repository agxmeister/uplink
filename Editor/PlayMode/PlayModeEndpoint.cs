using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Http;
using Newtonsoft.Json;

namespace Agxmeister.Uplink.PlayMode
{
    /// <summary>
    /// `POST /play`: puts the Editor into play mode and takes it out again, so that logs, screenshots and
    /// tests have a running game to be about.
    /// </summary>
    public sealed class PlayModeEndpoint : IEndpoint
    {
        private readonly IPlayModeControl control;

        public PlayModeEndpoint(IPlayModeControl control)
        {
            if (control == null)
            {
                throw new ArgumentNullException("control");
            }
            this.control = control;
        }

        public string Method
        {
            get { return "POST"; }
        }

        public string Path
        {
            get { return "/play"; }
        }

        public IDictionary<string, object> Describe()
        {
            return Schema.Operation(
                "set_play_mode",
                "Enter, leave, pause or step the Editor's play mode.",
                "Runs the game inside the Editor, which is what makes runtime behaviour observable: with " +
                "play mode on, `read_console` shows what the game logs and `screenshot` shows what it " +
                "draws.\n\n" +
                "Entering and leaving reload the Editor's script domain, so those answer `202` with " +
                "`state: \"changing\"`; call again with the same target until the answer is `done`. " +
                "Asking for something the Editor is already doing is answered `done` straight away, so " +
                "repeating the call is safe.\n\n" +
                "`play` resumes a paused game as well as starting a stopped one. `pause` and `step` need " +
                "play mode to be running already.",
                new Dictionary<string, object>
                {
                    { "200", Schema.JsonContent("The Editor is where it was asked to be.", StatusSchema()) },
                    { "202", Schema.JsonContent("On its way. Call again with the same target.", StatusSchema()) },
                    { "400", Schema.ErrorContent("The target was not understood, or needs play mode first.") },
                    { "504", Schema.ErrorContent("The Editor was too busy to answer. Retry.") },
                },
                null,
                Schema.JsonBody("What the Editor should do.", Schema.Object(new Dictionary<string, object>
                {
                    {
                        "target",
                        Schema.Choice(
                            "What to ask of play mode.", PlayModeTarget.All, PlayModeTarget.Play)
                    },
                }), false));
        }

        public Response Handle(Request request)
        {
            var wanted = new Arguments(request).Body<Wanted>();
            var target = wanted.Target == null
                ? PlayModeTarget.Play
                : wanted.Target.Trim().ToLowerInvariant();

            var status = control.Poll(target);

            return Response.Json(status.State == PlayModeCycle.Done ? 200 : 202, status);
        }

        private static IDictionary<string, object> StatusSchema()
        {
            return Schema.Object(new Dictionary<string, object>
            {
                {
                    "state",
                    Schema.Choice(
                        "Whether the Editor has arrived.",
                        new[] { PlayModeCycle.Changing, PlayModeCycle.Done }, null)
                },
                { "target", Schema.Choice("What was asked for.", PlayModeTarget.All, null) },
                { "isPlaying", Schema.Property("boolean", "Whether the Editor is in play mode.") },
                { "isPaused", Schema.Property("boolean", "Whether play mode is held still.") },
            });
        }

        /// <summary>The request body.</summary>
        private sealed class Wanted
        {
            [JsonProperty("target")]
            public string Target { get; set; }
        }
    }
}
