using UnityEngine;

namespace Agxmeister.Uplink.Diagnostics
{
    /// <summary>Writes to the Editor console, prefixed so Uplink's own messages are easy to filter out.</summary>
    public sealed class UnityLog : IUplinkLog
    {
        private const string Prefix = "[Uplink] ";

        public void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        public void Warning(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        public void Error(string message)
        {
            Debug.LogError(Prefix + message);
        }
    }
}
