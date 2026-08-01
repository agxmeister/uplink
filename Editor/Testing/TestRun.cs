using System.Collections.Generic;
using Newtonsoft.Json;

namespace Agxmeister.Uplink.Testing
{
    /// <summary>
    /// What `POST /tests` answers with. Property names are the wire contract described by
    /// <see cref="TestsEndpoint.Describe"/>.
    /// </summary>
    public sealed class TestRun
    {
        /// <summary>`running` while the tests are going, `done` when this is the outcome of a finished run.</summary>
        [JsonProperty("state")]
        public string State { get; set; }

        /// <summary>Which suite was run, `edit` or `play`.</summary>
        [JsonProperty("mode")]
        public string Mode { get; set; }

        /// <summary>
        /// Why the run could not happen at all — scripts that do not compile, most often. Absent when the
        /// tests themselves ran, however they went.
        /// </summary>
        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public string Error { get; set; }

        [JsonProperty("summary")]
        public TestSummary Summary { get; set; }

        /// <summary>Every test that did not pass, which is what a caller is nearly always after.</summary>
        [JsonProperty("failures")]
        public IList<TestOutcome> Failures { get; set; }

        /// <summary>Every test, absent unless `includePassed` asked for them.</summary>
        [JsonProperty("tests", NullValueHandling = NullValueHandling.Ignore)]
        public IList<TestOutcome> Tests { get; set; }
    }

    public sealed class TestSummary
    {
        [JsonProperty("passed")]
        public int Passed { get; set; }

        [JsonProperty("failed")]
        public int Failed { get; set; }

        [JsonProperty("skipped")]
        public int Skipped { get; set; }

        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }
    }

    /// <summary>How one test went.</summary>
    public sealed class TestOutcome
    {
        /// <summary>The test's full name, as the Test Runner window shows it.</summary>
        [JsonProperty("name")]
        public string Name { get; set; }

        /// <summary>One of `passed`, `failed`, `skipped`, `inconclusive`.</summary>
        [JsonProperty("status")]
        public string Status { get; set; }

        /// <summary>What the assertion said, on a test that did not pass.</summary>
        [JsonProperty("message", NullValueHandling = NullValueHandling.Ignore)]
        public string Message { get; set; }

        [JsonProperty("stackTrace", NullValueHandling = NullValueHandling.Ignore)]
        public string StackTrace { get; set; }

        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }
    }

    public static class TestState
    {
        public const string Passed = "passed";
        public const string Failed = "failed";
        public const string Skipped = "skipped";
        public const string Inconclusive = "inconclusive";

        public static readonly string[] All = { Passed, Failed, Skipped, Inconclusive };
    }

    public static class TestModes
    {
        public const string Edit = "edit";
        public const string Play = "play";

        public static readonly string[] All = { Edit, Play };
    }
}
