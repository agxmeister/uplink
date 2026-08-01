using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Api;
using Agxmeister.Uplink.Http;

namespace Agxmeister.Uplink.Testing
{
    /// <summary>
    /// `POST /tests`: runs the project's tests and reports which of them failed.
    /// </summary>
    public sealed class TestsEndpoint : IEndpoint
    {
        private readonly ITestRunner runner;

        public TestsEndpoint(ITestRunner runner)
        {
            if (runner == null)
            {
                throw new ArgumentNullException("runner");
            }
            this.runner = runner;
        }

        public string Method
        {
            get { return "POST"; }
        }

        public string Path
        {
            get { return "/tests"; }
        }

        public IDictionary<string, object> Describe()
        {
            return Schema.Operation(
                "run_tests",
                "Run the project's tests and report the failures.",
                "Runs the EditMode or PlayMode suite through the Unity Test Framework.\n\n" +
                "A suite takes far longer than one request, and a PlayMode run reloads the Editor's script " +
                "domain partway through, so call this repeatedly: the first call starts a run and answers " +
                "`202` with `state: \"running\"`, further calls answer the same while it goes, and once it " +
                "finishes one call returns `state: \"done\"` with the results. That result is handed over " +
                "once — the call after it starts a fresh run.\n\n" +
                "Narrow a run with `names`, `categories` or `assemblies`; leave them out to run everything. " +
                "Only failures are listed unless `includePassed` is set, and `summary` counts them all " +
                "either way. An `error` in the response means the run could not happen at all — check " +
                "`compile` first if so.",
                new Dictionary<string, object>
                {
                    { "200", Schema.JsonContent("A finished run, or one already under way.", RunSchema()) },
                    { "202", Schema.JsonContent("A run has begun. Call again for the results.", RunSchema()) },
                    { "400", Schema.ErrorContent("The request body was not understood.") },
                    { "504", Schema.ErrorContent("The Editor was too busy to answer. Retry.") },
                },
                null,
                Schema.JsonBody("Which tests to run.", Schema.Object(new Dictionary<string, object>
                {
                    { "mode", Schema.Choice("Which suite to run.", TestModes.All, TestModes.Edit) },
                    {
                        "names",
                        Schema.Array("Test names to run; all of them when empty.", Schema.Property("string", "A test name."))
                    },
                    {
                        "categories",
                        Schema.Array("Categories to run; all of them when empty.", Schema.Property("string", "A category."))
                    },
                    {
                        "assemblies",
                        Schema.Array("Test assemblies to run; all of them when empty.", Schema.Property("string", "An assembly name."))
                    },
                    {
                        "includePassed",
                        Schema.Property("boolean", "List every test rather than only the failures.", false)
                    },
                }), false));
        }

        public Response Handle(Request request)
        {
            var options = new Arguments(request).Body<TestRunOptions>();
            options.Mode = Mode(options.Mode);

            var run = runner.Poll(options);

            return Response.Json(run.State == TestLog.Done ? 200 : 202, run);
        }

        private static string Mode(string requested)
        {
            if (string.IsNullOrEmpty(requested))
            {
                return TestModes.Edit;
            }

            var normalized = requested.Trim().ToLowerInvariant();
            foreach (var mode in TestModes.All)
            {
                if (mode == normalized)
                {
                    return mode;
                }
            }

            throw new BadRequestException(string.Format(
                "'mode' must be one of {0}, not '{1}'.", string.Join(", ", TestModes.All), requested));
        }

        private static IDictionary<string, object> RunSchema()
        {
            return Schema.Object(new Dictionary<string, object>
            {
                {
                    "state",
                    Schema.Choice("Whether the run is still going.", new[] { TestLog.Running, TestLog.Done }, null)
                },
                { "mode", Schema.Choice("Which suite was run.", TestModes.All, null) },
                { "error", Schema.Property("string", "Why the run could not happen at all.") },
                {
                    "summary", Schema.Object(new Dictionary<string, object>
                    {
                        { "passed", Schema.Property("integer", "How many tests passed.") },
                        { "failed", Schema.Property("integer", "How many did not.") },
                        { "skipped", Schema.Property("integer", "How many were skipped.") },
                        { "total", Schema.Property("integer", "How many ran in all.") },
                        { "durationMs", Schema.Property("integer", "How long the run took.") },
                    })
                },
                { "failures", Schema.Array("Every test that did not pass.", OutcomeSchema()) },
                { "tests", Schema.Array("Every test, when 'includePassed' asked for them.", OutcomeSchema()) },
            });
        }

        private static IDictionary<string, object> OutcomeSchema()
        {
            return Schema.Object(new Dictionary<string, object>
            {
                { "name", Schema.Property("string", "The test's full name.") },
                { "status", Schema.Choice("How it went.", TestState.All, null) },
                { "message", Schema.Property("string", "What the assertion said.") },
                { "stackTrace", Schema.Property("string", "Where it failed.") },
                { "durationMs", Schema.Property("integer", "How long it took.") },
            });
        }
    }
}
