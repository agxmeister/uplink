using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Http;

namespace Agxmeister.Uplink.Compilation
{
    /// <summary>
    /// `POST /compile`: builds the project's scripts and reports what the compiler thought of them.
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
                "Compile the project's scripts and report any errors.",
                "Call this after editing C# to find out whether it builds.\n\n" +
                "Compiling reloads the Editor's script domain, so one call cannot both start a build and " +
                "report it. Call this repeatedly instead: the first call starts a compile and answers " +
                "`202` with `state: \"compiling\"`, further calls answer the same while it runs, and once " +
                "it finishes one call returns `state: \"done\"` with the errors and warnings. That " +
                "result is handed over once — the call after it starts a fresh compile.\n\n" +
                "`changed: false` means nothing needed rebuilding, and the messages returned are the ones " +
                "already standing rather than the product of a new build. Only scripts are compiled; " +
                "runtime errors appear in `read_console` instead.",
                new Dictionary<string, object>
                {
                    { "200", Schema.JsonContent("A finished compile, or one already under way.", ResultSchema()) },
                    { "202", Schema.JsonContent("A compile has begun. Call again for the outcome.", ResultSchema()) },
                    { "504", Schema.ErrorContent("The Editor was too busy to answer. Retry.") },
                },
                null, null);
        }

        public Response Handle(Request request)
        {
            var result = compiler.Poll();

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
                    Schema.Choice("Whether the build is still going.", new[] { CompileLog.Compiling, CompileLog.Done }, null)
                },
                { "changed", Schema.Property("boolean", "Whether anything actually needed rebuilding.") },
                { "errors", Schema.Array("Compiler errors, at most 100.", MessageSchema()) },
                { "warnings", Schema.Array("Compiler warnings, at most 100.", MessageSchema()) },
                { "errorCount", Schema.Property("integer", "How many errors there were in total.") },
                { "warningCount", Schema.Property("integer", "How many warnings there were in total.") },
                { "durationMs", Schema.Property("integer", "How long the build took, or 0 while it runs.") },
            });
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
    }
}
