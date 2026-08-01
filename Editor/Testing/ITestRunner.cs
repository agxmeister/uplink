using Newtonsoft.Json;

namespace Agxmeister.Uplink.Testing
{
    /// <summary>
    /// Runs the project's tests and says how they went. One method, for the same reason as
    /// <see cref="Agxmeister.Uplink.Compilation.ICompiler"/>: the tool is one call made repeatedly.
    /// </summary>
    public interface ITestRunner
    {
        /// <summary>
        /// Starts a run if none is under way, and reports where things stand. <paramref name="options"/> is
        /// what a new run is started with; a call that finds one already going ignores all of it but
        /// <see cref="TestRunOptions.IncludePassed"/>, which only shapes the answer.
        /// </summary>
        TestRun Poll(TestRunOptions options);
    }

    /// <summary>The request body of `POST /tests`.</summary>
    public sealed class TestRunOptions
    {
        public TestRunOptions()
        {
            Mode = TestModes.Edit;
        }

        /// <summary>Which suite to run: `edit` or `play`.</summary>
        [JsonProperty("mode")]
        public string Mode { get; set; }

        /// <summary>Full or partial test names to run; all of them when empty.</summary>
        [JsonProperty("names")]
        public string[] Names { get; set; }

        /// <summary>NUnit categories to run; all of them when empty.</summary>
        [JsonProperty("categories")]
        public string[] Categories { get; set; }

        /// <summary>Test assemblies to run; all of them when empty.</summary>
        [JsonProperty("assemblies")]
        public string[] Assemblies { get; set; }

        /// <summary>Report every test rather than only the ones that did not pass.</summary>
        [JsonProperty("includePassed")]
        public bool IncludePassed { get; set; }
    }
}
