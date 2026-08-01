using System;
using Agxmeister.Uplink.Persistence;
using Agxmeister.Uplink.Services;
using UnityEngine;

namespace Agxmeister.Uplink.Console
{
    /// <summary>
    /// The one place that listens to Unity's logging. It has to be running before a request arrives — a
    /// message is gone by the time anyone asks for it — so it is a service rather than part of the endpoint.
    ///
    /// It also owns the buffer's journey across a domain reload: the buffer goes into the session store on
    /// the way out and comes back on the way in, which is why `seq` keeps counting rather than restarting.
    /// </summary>
    public sealed class ConsoleCollector : IUplinkService
    {
        private const string StateKey = "console";

        private readonly ConsoleBuffer buffer;
        private readonly ISessionStore store;
        private readonly IConsoleHistory history;

        private bool listening;

        public ConsoleCollector(ConsoleBuffer buffer, ISessionStore store, IConsoleHistory history)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }
            if (store == null)
            {
                throw new ArgumentNullException("store");
            }

            this.buffer = buffer;
            this.store = store;
            this.history = history;
        }

        public void Attach()
        {
            var state = Stored.Read<ConsoleState>(store, StateKey);
            buffer.Restore(state);

            if (state == null)
            {
                // Nothing carried over, so this is the session's first load and the Console may hold messages
                // Uplink was not there to hear. Seeding only here keeps one message from being recorded twice.
                Seed();
            }

            // Threaded: Unity delivers a message on whichever thread logged it, and ignoring the ones that did
            // not come from the main thread would lose exactly the failures worth seeing.
            Application.logMessageReceivedThreaded += Record;
            listening = true;
        }

        public void Detach()
        {
            if (listening)
            {
                Application.logMessageReceivedThreaded -= Record;
                listening = false;
            }

            Stored.Write(store, StateKey, buffer.Capture());
        }

        private void Seed()
        {
            if (history == null)
            {
                return;
            }

            var entries = history.Read();
            buffer.HistoryAvailable = entries != null;
            buffer.Seed(entries);
        }

        private void Record(string message, string stackTrace, LogType type)
        {
            buffer.Record(ConsoleLevel.Of(type), message, stackTrace, DateTime.UtcNow);
        }
    }
}
