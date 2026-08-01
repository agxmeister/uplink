using UnityEngine;

namespace Agxmeister.Uplink.Console
{
    /// <summary>
    /// The three severities the API exposes, and the one place Unity's many ways of saying "error" are
    /// collapsed into them.
    /// </summary>
    public static class ConsoleLevel
    {
        public const string Error = "error";
        public const string Warning = "warning";
        public const string Log = "log";

        /// <summary>Every value <c>level</c> accepts, ordered least to most severe.</summary>
        public static readonly string[] All = { Log, Warning, Error };

        public static string Of(LogType type)
        {
            switch (type)
            {
                case LogType.Error:
                case LogType.Exception:
                case LogType.Assert:
                    return Error;
                case LogType.Warning:
                    return Warning;
                default:
                    return Log;
            }
        }

        /// <summary>How severe a level is, so `level` can mean "this severity or worse".</summary>
        public static int Severity(string level)
        {
            switch (level)
            {
                case Error:
                    return 2;
                case Warning:
                    return 1;
                default:
                    return 0;
            }
        }
    }
}
