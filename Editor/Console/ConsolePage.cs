using System.Collections.Generic;
using Newtonsoft.Json;

namespace Agxmeister.Uplink.Console
{
    /// <summary>What `GET /console` answers with: a window onto the message stream, plus where to read next.</summary>
    public sealed class ConsolePage
    {
        [JsonProperty("entries")]
        public IList<ConsoleEntry> Entries { get; set; }

        /// <summary>
        /// The `since` to pass next time. Everything below it has been accounted for, so a client that keeps
        /// this value sees each message exactly once.
        /// </summary>
        [JsonProperty("nextSince")]
        public long NextSince { get; set; }

        /// <summary>More matched than `limit`; ask again with the returned `nextSince`.</summary>
        [JsonProperty("truncated")]
        public bool Truncated { get; set; }

        /// <summary>
        /// Whether messages logged before Uplink loaded could be recovered from the Editor's own Console.
        /// False means the stream starts where Uplink started.
        /// </summary>
        [JsonProperty("historyAvailable")]
        public bool HistoryAvailable { get; set; }

        /// <summary>
        /// How many messages of each level matched, before `level` and `limit` narrowed them — so a client
        /// asking only for errors still learns that there are warnings.
        /// </summary>
        [JsonProperty("counts")]
        public ConsoleCounts Counts { get; set; }
    }

    public sealed class ConsoleCounts
    {
        [JsonProperty("errors")]
        public int Errors { get; set; }

        [JsonProperty("warnings")]
        public int Warnings { get; set; }

        [JsonProperty("logs")]
        public int Logs { get; set; }

        public void Add(string level)
        {
            switch (level)
            {
                case ConsoleLevel.Error:
                    Errors++;
                    break;
                case ConsoleLevel.Warning:
                    Warnings++;
                    break;
                default:
                    Logs++;
                    break;
            }
        }
    }
}
