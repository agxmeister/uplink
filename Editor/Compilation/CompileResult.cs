using System.Collections.Generic;
using Newtonsoft.Json;

namespace Agxmeister.Uplink.Compilation
{
    /// <summary>
    /// What `POST /compile` answers with. Property names are the wire contract described by
    /// <see cref="CompileEndpoint.Describe"/>.
    /// </summary>
    public sealed class CompileResult
    {
        /// <summary>`compiling` while the Editor is working, `done` when this is the outcome of a finished run.</summary>
        [JsonProperty("state")]
        public string State { get; set; }

        /// <summary>
        /// Whether anything actually needed recompiling. False means the scripts on disk were already built,
        /// and the messages below are the ones that were already standing.
        /// </summary>
        [JsonProperty("changed")]
        public bool Changed { get; set; }

        /// <summary>Every error the compiler produced, capped; <see cref="ErrorCount"/> is the true total.</summary>
        [JsonProperty("errors")]
        public IList<CompileMessage> Errors { get; set; }

        [JsonProperty("warnings")]
        public IList<CompileMessage> Warnings { get; set; }

        [JsonProperty("errorCount")]
        public int ErrorCount { get; set; }

        [JsonProperty("warningCount")]
        public int WarningCount { get; set; }

        /// <summary>How long the run took, or 0 while it is still going.</summary>
        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }
    }

    /// <summary>One thing the compiler had to say, and where.</summary>
    public sealed class CompileMessage
    {
        /// <summary>Path of the source file, relative to the project.</summary>
        [JsonProperty("file")]
        public string File { get; set; }

        [JsonProperty("line")]
        public int Line { get; set; }

        [JsonProperty("column")]
        public int Column { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        /// <summary>The assembly being built when this came up.</summary>
        [JsonProperty("assembly")]
        public string Assembly { get; set; }

        /// <summary>`error` or `warning`.</summary>
        [JsonProperty("level")]
        public string Level { get; set; }
    }
}
