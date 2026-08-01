namespace Agxmeister.Uplink.Console
{
    /// <summary>
    /// Reads messages the Editor has produced. Keeps the endpoint away from both Unity's logging callbacks
    /// and the buffer behind them, so it can be tested against a stand-in.
    /// </summary>
    public interface IConsoleReader
    {
        ConsolePage Read(ConsoleQuery query);
    }

    /// <summary>What the client asked for, gathered into one value rather than five parameters.</summary>
    public sealed class ConsoleQuery
    {
        public ConsoleQuery()
        {
            Level = ConsoleLevel.Log;
            Limit = 100;
            StackTraces = true;
        }

        /// <summary>Minimum severity to return; <see cref="ConsoleLevel.Log"/> returns everything.</summary>
        public string Level { get; set; }

        /// <summary>Return only messages at or after this position in the stream.</summary>
        public long Since { get; set; }

        public int Limit { get; set; }

        /// <summary>Return only messages containing this text, case-insensitively.</summary>
        public string Search { get; set; }

        /// <summary>Include stack traces on errors, which is the only level where they carry anything.</summary>
        public bool StackTraces { get; set; }
    }
}
