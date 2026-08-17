using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Http;

namespace Agxmeister.Uplink.Controls
{
    /// <summary>
    /// `GET /input`: the read half of the input cycle — where it stands and what the last script did, without
    /// starting one or consuming a result waiting to be handed over.
    ///
    /// Born beside <see cref="InputEndpoint"/> rather than acquired after somebody got bitten, which is what
    /// ADR-0012 asks of a cycle endpoint: because the acting call is also the polling call, one poll too many
    /// after `done` would play a script nobody asked for.
    /// </summary>
    public sealed class InputStatusEndpoint : IEndpoint
    {
        private readonly IInputDriver driver;

        public InputStatusEndpoint(IInputDriver driver)
        {
            if (driver == null)
            {
                throw new ArgumentNullException("driver");
            }
            this.driver = driver;
        }

        public string Method
        {
            get { return "GET"; }
        }

        public string Path
        {
            get { return "/input"; }
        }

        public IDictionary<string, object> Describe()
        {
            return Schema.Operation(
                "input_status",
                "Report where the input cycle stands, without playing anything.",
                "Answers the same result `play_input` does, but only observes: it never starts a script and " +
                "never consumes the outcome of one. Any number of these calls in a row leave the Editor " +
                "exactly as they found it, which makes this the safe way to wait for a script to play out.\n\n" +
                "`state` says which of three places the cycle is in. `running` — a script is playing; the " +
                "answer is `202`, and `stepsDelivered` of `steps` says how far it has got. `done` — a " +
                "script has played out and its outcome is here, `200`, waiting for a `play_input` call to " +
                "take delivery of it. `idle` — nothing is playing and nothing is waiting, `200`: the next " +
                "`play_input` call would start something new.\n\n" +
                "Everything this returns is marked `stale: true`, because looking is not taking delivery. " +
                "It works outside play mode too, where it reports `isPlaying: false` and whatever the last " +
                "script left standing — including `playModeEnded`, if that is how the last one finished.",
                new Dictionary<string, object>
                {
                    {
                        "200", Schema.JsonContent(
                            "Nothing is playing: either an outcome is waiting to be handed over (`done`) or " +
                            "the cycle is at rest (`idle`).",
                            InputEndpoint.ResultSchema(true))
                    },
                    {
                        "202", Schema.JsonContent(
                            "A script is playing. Call again — with this or with `play_input` — for the outcome.",
                            InputEndpoint.ResultSchema(true))
                    },
                    { "504", Schema.ErrorContent("The Editor was too busy to answer. Retry.") },
                },
                null,
                null);
        }

        public Response Handle(Request request)
        {
            var result = driver.Peek();

            // The same 202-means-not-finished rule the acting call follows, so a poll loop can use either
            // verb while it waits without reading the body.
            return Response.Json(result.State == InputScript.Running ? 202 : 200, result);
        }
    }
}
