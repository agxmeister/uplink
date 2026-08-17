using System.Collections.Generic;
using Newtonsoft.Json;

namespace Agxmeister.Uplink.Controls
{
    /// <summary>One step of a script, as a client writes it.</summary>
    public sealed class InputStep
    {
        /// <summary>A key: `space`, or the Input System's own `&lt;Keyboard&gt;/space`.</summary>
        [JsonProperty("key")]
        public string Key { get; set; }

        /// <summary>A mouse button: `left`, `right`, `middle`, or `&lt;Mouse&gt;/leftButton`.</summary>
        [JsonProperty("click")]
        public string Click { get; set; }

        /// <summary>Where to put the pointer, in Game-view pixels from the top-left.</summary>
        [JsonProperty("move")]
        public float[] Move { get; set; }

        /// <summary>Seconds to let pass, doing nothing.</summary>
        [JsonProperty("wait")]
        public double? Wait { get; set; }

        /// <summary>Seconds to hold a key or button down.</summary>
        [JsonProperty("hold")]
        public double? Hold { get; set; }
    }

    /// <summary>The request body of `POST /input`.</summary>
    public sealed class InputRequest
    {
        [JsonProperty("steps")]
        public IList<InputStep> Steps { get; set; }
    }

    /// <summary>Where the input cycle stands, and what it has actually delivered.</summary>
    public sealed class InputResult
    {
        [JsonProperty("state")]
        public string State { get; set; }

        [JsonProperty("steps")]
        public int Steps { get; set; }

        [JsonProperty("stepsDelivered")]
        public int StepsDelivered { get; set; }

        [JsonProperty("elapsedMs")]
        public long ElapsedMs { get; set; }

        [JsonProperty("durationMs")]
        public long DurationMs { get; set; }

        [JsonProperty("playModeEnded")]
        public bool PlayModeEnded { get; set; }

        [JsonProperty("isPlaying")]
        public bool IsPlaying { get; set; }

        /// <summary>
        /// The surface a pointer lands on. Absent outside play mode, where there is no Game view being drawn
        /// and therefore no honest size to report.
        /// </summary>
        [JsonProperty("gameView", NullValueHandling = NullValueHandling.Ignore)]
        public ViewSize GameView { get; set; }

        [JsonProperty("note", NullValueHandling = NullValueHandling.Ignore)]
        public string Note { get; set; }

        /// <summary>Present and true when this was looked at rather than handed over. See ADR-0012.</summary>
        [JsonProperty("stale", NullValueHandling = NullValueHandling.Ignore)]
        public bool? Stale { get; set; }
    }

    public sealed class ViewSize
    {
        [JsonProperty("width")]
        public int Width { get; set; }

        [JsonProperty("height")]
        public int Height { get; set; }
    }
}
