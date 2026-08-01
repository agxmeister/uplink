using Newtonsoft.Json;

namespace Agxmeister.Uplink.PlayMode
{
    /// <summary>
    /// What `POST /play` answers with. Property names are the wire contract described by
    /// <see cref="PlayModeEndpoint.Describe"/>.
    /// </summary>
    public sealed class PlayModeStatus
    {
        /// <summary>`changing` while the Editor is on its way, `done` once it has arrived.</summary>
        [JsonProperty("state")]
        public string State { get; set; }

        /// <summary>What was asked for, echoed back.</summary>
        [JsonProperty("target")]
        public string Target { get; set; }

        [JsonProperty("isPlaying")]
        public bool IsPlaying { get; set; }

        [JsonProperty("isPaused")]
        public bool IsPaused { get; set; }
    }

    /// <summary>What a caller can ask the Editor to do.</summary>
    public static class PlayModeTarget
    {
        /// <summary>Enter play mode, or resume it if it is paused.</summary>
        public const string Play = "play";

        /// <summary>Leave play mode.</summary>
        public const string Stop = "stop";

        /// <summary>Hold play mode still.</summary>
        public const string Pause = "pause";

        /// <summary>Advance a single frame while paused.</summary>
        public const string Step = "step";

        public static readonly string[] All = { Play, Stop, Pause, Step };
    }

    public static class PlayModeCycle
    {
        public const string Changing = "changing";
        public const string Done = "done";
    }
}
