using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Agxmeister.Uplink.Console
{
    /// <summary>
    /// The messages of the current session, oldest first, capped so a chatty project cannot grow it without
    /// bound. Deliberately free of any Unity dependency: <see cref="ConsoleCollector"/> feeds it, and it can
    /// be filled by hand in tests.
    ///
    /// Every method is locked because Unity delivers log messages from whatever thread produced them, while
    /// requests are served from the thread pool.
    /// </summary>
    public sealed class ConsoleBuffer : IConsoleReader
    {
        private readonly object gate = new object();
        private readonly List<ConsoleEntry> entries = new List<ConsoleEntry>();
        private readonly int capacity;

        private long nextSeq;

        public ConsoleBuffer() : this(1000)
        {
        }

        public ConsoleBuffer(int capacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException("capacity");
            }
            this.capacity = capacity;
        }

        /// <summary>
        /// Whether the messages from before Uplink loaded are in here. Set once, when the buffer is seeded,
        /// and carried across reloads so a client is told the same thing all session.
        /// </summary>
        public bool HistoryAvailable { get; set; }

        /// <summary>Appends a message, stamping it with its position in the stream.</summary>
        public void Record(string level, string message, string stackTrace, DateTime time)
        {
            lock (gate)
            {
                entries.Add(new ConsoleEntry
                {
                    Seq = nextSeq++,
                    Time = time.ToUniversalTime().ToString("o"),
                    Level = level,
                    Message = message ?? string.Empty,
                    StackTrace = string.IsNullOrEmpty(stackTrace) ? null : stackTrace,
                });

                if (entries.Count > capacity)
                {
                    entries.RemoveRange(0, entries.Count - capacity);
                }
            }
        }

        /// <summary>
        /// Appends messages that were logged before this buffer existed, keeping their order and whatever
        /// they know about themselves — unlike <see cref="Record"/>, which stamps the present moment.
        /// </summary>
        public void Seed(IList<ConsoleEntry> history)
        {
            if (history == null)
            {
                return;
            }

            lock (gate)
            {
                foreach (var entry in history)
                {
                    entry.Seq = nextSeq++;
                    entries.Add(entry);
                }

                if (entries.Count > capacity)
                {
                    entries.RemoveRange(0, entries.Count - capacity);
                }
            }
        }

        public ConsolePage Read(ConsoleQuery query)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            lock (gate)
            {
                var counts = new ConsoleCounts();
                var matched = new List<ConsoleEntry>();
                var wanted = ConsoleLevel.Severity(query.Level);

                foreach (var entry in entries)
                {
                    if (entry.Seq < query.Since || !Contains(entry.Message, query.Search))
                    {
                        continue;
                    }

                    // Counted before the severity filter, so a client asking only for errors still learns
                    // that warnings are waiting for it.
                    counts.Add(entry.Level);

                    if (ConsoleLevel.Severity(entry.Level) >= wanted)
                    {
                        matched.Add(entry);
                    }
                }

                var truncated = matched.Count > query.Limit;
                var page = truncated ? matched.GetRange(0, query.Limit) : matched;

                return new ConsolePage
                {
                    Entries = Present(page, query.StackTraces),
                    // Paging forward from the last message returned rather than from the end of the buffer:
                    // a truncated read must not skip what it did not have room for.
                    NextSince = truncated ? page[page.Count - 1].Seq + 1 : nextSeq,
                    Truncated = truncated,
                    HistoryAvailable = HistoryAvailable,
                    Counts = counts,
                };
            }
        }

        /// <summary>The buffer's state, to be handed across a domain reload.</summary>
        public ConsoleState Capture()
        {
            lock (gate)
            {
                return new ConsoleState
                {
                    Entries = new List<ConsoleEntry>(entries),
                    NextSeq = nextSeq,
                    HistoryAvailable = HistoryAvailable,
                };
            }
        }

        public void Restore(ConsoleState state)
        {
            if (state == null)
            {
                return;
            }

            lock (gate)
            {
                entries.Clear();
                if (state.Entries != null)
                {
                    entries.AddRange(state.Entries);
                }
                nextSeq = Math.Max(state.NextSeq, entries.Count == 0 ? 0 : entries[entries.Count - 1].Seq + 1);
                HistoryAvailable = state.HistoryAvailable;
            }
        }

        /// <summary>
        /// Copies each entry so that hiding a stack trace from one client does not erase it for the next, and
        /// drops traces for anything below error level, where they are noise rather than evidence.
        /// </summary>
        private static IList<ConsoleEntry> Present(IList<ConsoleEntry> page, bool stackTraces)
        {
            var presented = new List<ConsoleEntry>(page.Count);
            foreach (var entry in page)
            {
                presented.Add(new ConsoleEntry
                {
                    Seq = entry.Seq,
                    Time = entry.Time,
                    Level = entry.Level,
                    Message = entry.Message,
                    StackTrace = stackTraces && entry.Level == ConsoleLevel.Error ? entry.StackTrace : null,
                });
            }
            return presented;
        }

        private static bool Contains(string message, string search)
        {
            return string.IsNullOrEmpty(search)
                || (message != null && message.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }

    /// <summary>A buffer as stored between domain reloads.</summary>
    public sealed class ConsoleState
    {
        [JsonProperty("entries")]
        public IList<ConsoleEntry> Entries { get; set; }

        [JsonProperty("nextSeq")]
        public long NextSeq { get; set; }

        [JsonProperty("historyAvailable")]
        public bool HistoryAvailable { get; set; }
    }
}
