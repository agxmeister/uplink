using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Http;

namespace Agxmeister.Uplink.Console
{
    /// <summary>
    /// `GET /console`: what the Editor has said, as a stream a client can read forward through.
    /// </summary>
    public sealed class ConsoleEndpoint : IEndpoint
    {
        private const int MaxLimit = 500;

        private readonly IConsoleReader reader;

        public ConsoleEndpoint(IConsoleReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException("reader");
            }
            this.reader = reader;
        }

        public string Method
        {
            get { return "GET"; }
        }

        public string Path
        {
            get { return "/console"; }
        }

        public IDictionary<string, object> Describe()
        {
            return Schema.Operation(
                "read_console",
                "Read messages the Unity Editor has logged.",
                "Returns Editor console messages — errors, warnings and logs — oldest first.\n\n" +
                "Messages form a numbered stream. Every response carries `nextSince`; pass it back as " +
                "`since` on the next call to see only what has happened in between. Read `nextSince` " +
                "before making a change, then read again afterwards, and the result is exactly what that " +
                "change produced. When `truncated` is true there is more waiting: call again with the " +
                "returned `nextSince`.\n\n" +
                "`counts` reports every level that matched, whatever `level` was asked for, so a call for " +
                "errors alone still reveals that warnings exist.",
                new Dictionary<string, object>
                {
                    { "200", Schema.JsonContent("The matching messages.", PageSchema()) },
                    { "400", Schema.ErrorContent("A parameter was not understood.") },
                },
                new List<object>
                {
                    Schema.QueryParameter(
                        "level", "Minimum severity to return; 'log' returns everything.",
                        Schema.Choice("Minimum severity.", ConsoleLevel.All, ConsoleLevel.Log), false),
                    Schema.QueryParameter(
                        "since", "Return only messages at or after this position in the stream.",
                        Schema.Property("integer", "A 'nextSince' from an earlier call.", 0), false),
                    Schema.QueryParameter(
                        "limit", "How many messages to return at most.",
                        Schema.Property("integer", "Between 1 and 500.", 100), false),
                    Schema.QueryParameter(
                        "search", "Return only messages containing this text, case-insensitively.",
                        Schema.Property("string", "Text to look for."), false),
                    Schema.QueryParameter(
                        "stackTraces", "Include stack traces on errors, where they say where it happened.",
                        Schema.Property("boolean", "Whether to include stack traces.", true), false),
                },
                null);
        }

        public Response Handle(Request request)
        {
            var arguments = new Arguments(request);

            return Response.Json(200, reader.Read(new ConsoleQuery
            {
                Level = arguments.Choice("level", ConsoleLevel.Log, ConsoleLevel.All),
                Since = arguments.Long("since", 0, 0),
                Limit = arguments.Int("limit", 100, 1, MaxLimit),
                Search = arguments.String("search", null),
                StackTraces = arguments.Bool("stackTraces", true),
            }));
        }

        private static IDictionary<string, object> PageSchema()
        {
            return Schema.Object(new Dictionary<string, object>
            {
                {
                    "entries", Schema.Array("The matching messages, oldest first.", Schema.Object(
                        new Dictionary<string, object>
                        {
                            { "seq", Schema.Property("integer", "Position in the session's message stream.") },
                            { "time", Schema.Property("string", "When it was logged, ISO-8601 UTC.") },
                            { "level", Schema.Choice("Severity.", ConsoleLevel.All, null) },
                            { "message", Schema.Property("string", "The message itself.") },
                            { "stackTrace", Schema.Property("string", "Where an error came from.") },
                        }))
                },
                { "nextSince", Schema.Property("integer", "Pass as 'since' next time to see only what is new.") },
                { "truncated", Schema.Property("boolean", "More matched than 'limit'; call again with 'nextSince'.") },
                {
                    "historyAvailable",
                    Schema.Property("boolean", "Whether messages from before Uplink loaded could be recovered.")
                },
                {
                    "counts", Schema.Object(new Dictionary<string, object>
                    {
                        { "errors", Schema.Property("integer", "Errors that matched, whatever 'level' asked for.") },
                        { "warnings", Schema.Property("integer", "Warnings that matched.") },
                        { "logs", Schema.Property("integer", "Plain logs that matched.") },
                    })
                },
            });
        }
    }
}
