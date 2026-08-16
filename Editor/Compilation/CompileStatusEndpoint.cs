using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Http;

namespace Agxmeister.Uplink.Compilation
{
    /// <summary>
    /// `GET /compile`: the read half of the compile cycle — where it stands, and what the last run said,
    /// without starting a run or consuming a result waiting to be handed over.
    ///
    /// It shares its path with <see cref="CompileEndpoint"/> deliberately: one tool with two modes, the verb
    /// saying whether the caller means to act or only to look. A poll loop written with GET cannot poll one
    /// call too many and set off a build nobody asked for.
    /// </summary>
    public sealed class CompileStatusEndpoint : IEndpoint
    {
        private readonly ICompiler compiler;

        public CompileStatusEndpoint(ICompiler compiler)
        {
            if (compiler == null)
            {
                throw new ArgumentNullException("compiler");
            }
            this.compiler = compiler;
        }

        public string Method
        {
            get { return "GET"; }
        }

        public string Path
        {
            get { return "/compile"; }
        }

        public IDictionary<string, object> Describe()
        {
            return Schema.Operation(
                "compile_status",
                "Report where the compile cycle stands, without compiling anything.",
                "Answers the same result `compile` does, but only observes: it never starts a build, and " +
                "never consumes the outcome of one. Any number of these calls in a row leave the Editor " +
                "exactly as they found it, which makes this the safe way to poll while waiting.\n\n" +
                "`state` says which of three places the cycle is in. `compiling` — a run is under way; the " +
                "answer is `202`, so waiting means calling again. `done` — a run has finished and its " +
                "result is here, `200`, waiting for a `compile` call to take delivery of it. `idle` — " +
                "nothing is running and nothing is waiting, `200`, with whatever the last run left standing: " +
                "the next `compile` call would start a fresh build.\n\n" +
                "Everything this returns is marked `stale: true`, because looking is not taking delivery — " +
                "the one-shot hand-over belongs to `compile`, and a result read here can be read again. Use " +
                "`compile` to start a run and to collect its result (which carries the `console` page of " +
                "what the run logged); use this to watch in between, or to ask what happened without " +
                "risking a build.",
                new Dictionary<string, object>
                {
                    {
                        "200", Schema.JsonContent(
                            "Nothing is being built: either a finished result is waiting to be handed over " +
                            "(`done`) or the cycle is at rest (`idle`).",
                            CompileEndpoint.ResultSchema(true))
                    },
                    {
                        "202", Schema.JsonContent(
                            "A run is under way. Call again — with this or with `compile` — for the outcome.",
                            CompileEndpoint.ResultSchema(true))
                    },
                    { "504", Schema.ErrorContent("The Editor was too busy to answer. Retry.") },
                },
                null,
                null);
        }

        public Response Handle(Request request)
        {
            var result = compiler.Peek();

            // The same 202-means-not-finished rule the acting call follows, so a poll loop can use either
            // verb while it waits without reading the body.
            return Response.Json(result.State == CompileLog.Compiling ? 202 : 200, result);
        }
    }
}
