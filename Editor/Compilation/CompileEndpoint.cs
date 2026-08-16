using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Console;
using Agxmeister.Uplink.Http;
using Newtonsoft.Json;

namespace Agxmeister.Uplink.Compilation
{
    /// <summary>
    /// `POST /compile`: builds the project's scripts, follows the domain reload the build causes, and reports
    /// what the compiler said and what the reload logged.
    /// </summary>
    public sealed class CompileEndpoint : IEndpoint
    {
        private readonly ICompiler compiler;

        public CompileEndpoint(ICompiler compiler)
        {
            if (compiler == null)
            {
                throw new ArgumentNullException("compiler");
            }
            this.compiler = compiler;
        }

        public string Method
        {
            get { return "POST"; }
        }

        public string Path
        {
            get { return "/compile"; }
        }

        public IDictionary<string, object> Describe()
        {
            return Schema.Operation(
                "compile",
                "Compile the project's scripts, reload, and report errors and what the reload logged.",
                "Call this after editing C# to find out whether it builds — and to run it, when \"it\" is " +
                "Editor code: the domain reload a successful build causes is what re-runs " +
                "[InitializeOnLoadMethod] setup scripts.\n\n" +
                "That reload closes the listener, so one call cannot both start the work and report it. " +
                "Call this repeatedly instead: the first call starts a run and answers `202` with " +
                "`state: \"compiling\"`, further calls answer the same while it works, and once the build " +
                "*and its reload* have finished one call returns `state: \"done\"`. The `done` result " +
                "carries `console` — everything the Editor logged during the run, the same objects " +
                "`read_console` returns — so what a setup script printed arrives with the result and needs " +
                "no separate console read. That result is handed over once; the call after it starts a " +
                "fresh run.\n\n" +
                "`changed: false` means nothing needed rebuilding, so no reload happened and no setup code " +
                "ran. To re-run it anyway — the next stage of a staged setup script, say — post " +
                "`{\"force\": true}`, which reloads the domain even when no script changed; the result " +
                "then reports `changed: false, forced: true`, telling a forced reload from a real " +
                "rebuild.\n\n" +
                "Watch `isPlaying`: while the Editor is in play mode, reloads still happen but " +
                "[InitializeOnLoadMethod] code silently does nothing, and a `done` with an empty `console` " +
                "is the symptom. The result says so in `note` when it applies. Only scripts are compiled; " +
                "later runtime errors appear in `read_console`.",
                new Dictionary<string, object>
                {
                    { "200", Schema.JsonContent("A finished run: the build's messages and the reload's log.", ResultSchema()) },
                    { "202", Schema.JsonContent("A run has begun or is under way. Call again for the outcome.", ResultSchema()) },
                    { "504", Schema.ErrorContent("The Editor was too busy to answer. Retry.") },
                },
                null,
                Schema.JsonBody("What kind of run to start; only read when this call starts one.", Schema.Object(
                    new Dictionary<string, object>
                    {
                        {
                            "force",
                            Schema.Property(
                                "boolean",
                                "Reload the script domain even when no script changed, re-running " +
                                "[InitializeOnLoadMethod] setup code without touching a file.",
                                false)
                        },
                    }), false));
        }

        public Response Handle(Request request)
        {
            var wanted = new Arguments(request).Body<Wanted>();
            var result = compiler.Poll(wanted.Force.HasValue && wanted.Force.Value);

            // 202 rather than 200 while it works, so a client can tell "not finished" from "finished" without
            // reading the body.
            return Response.Json(result.State == CompileLog.Done ? 200 : 202, result);
        }

        private static IDictionary<string, object> ResultSchema()
        {
            return Schema.Object(new Dictionary<string, object>
            {
                {
                    "state",
                    Schema.Choice("Whether the run is still going.", new[] { CompileLog.Compiling, CompileLog.Done }, null)
                },
                { "changed", Schema.Property("boolean", "Whether anything actually needed rebuilding.") },
                { "forced", Schema.Property("boolean", "Whether the run was asked to reload regardless.") },
                { "errors", Schema.Array("Compiler errors, at most 100.", MessageSchema()) },
                { "warnings", Schema.Array("Compiler warnings, at most 100.", MessageSchema()) },
                { "errorCount", Schema.Property("integer", "How many errors there were in total.") },
                { "warningCount", Schema.Property("integer", "How many warnings there were in total.") },
                { "durationMs", Schema.Property("integer", "How long the run took, or 0 while it runs.") },
                {
                    "isPlaying",
                    Schema.Property(
                        "boolean",
                        "Whether the Editor is in play mode right now. While it is, reloads run no " +
                        "[InitializeOnLoadMethod] code — leave play mode and force a run to re-run it.")
                },
                {
                    "note",
                    Schema.Property("string", "A warning the numbers would hide, present only when it applies.")
                },
                { "console", ReloadConsoleSchema() },
            });
        }

        private static IDictionary<string, object> ReloadConsoleSchema()
        {
            var page = ConsoleEndpoint.PageSchema();
            page["description"] =
                "What the Editor logged during the run — the reload's [InitializeOnLoadMethod] output " +
                "above all — as read_console would return it, minus Uplink's own chatter. Only on a " +
                "finished run.";
            return page;
        }

        private static IDictionary<string, object> MessageSchema()
        {
            return Schema.Object(new Dictionary<string, object>
            {
                { "file", Schema.Property("string", "Source file, relative to the project.") },
                { "line", Schema.Property("integer", "Line the compiler pointed at.") },
                { "column", Schema.Property("integer", "Column the compiler pointed at.") },
                { "message", Schema.Property("string", "What the compiler said.") },
                { "assembly", Schema.Property("string", "The assembly being built at the time.") },
                {
                    "level",
                    Schema.Choice("Severity.", new[] { CompileLevel.Error, CompileLevel.Warning }, null)
                },
            });
        }

        /// <summary>The request body.</summary>
        private sealed class Wanted
        {
            [JsonProperty("force")]
            public bool? Force { get; set; }
        }
    }
}
