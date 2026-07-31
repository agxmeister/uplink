using UnityEditor;

namespace Agxmeister.Uplink.Configuration
{
    /// <summary>
    /// Settings kept in EditorPrefs, which are per-machine rather than per-project — the right scope for a
    /// port, since two Editors on one machine cannot share one.
    /// </summary>
    public sealed class EditorPrefsSettings : IUplinkSettings
    {
        public const int DefaultPort = 8787;

        private const string PortKey = "Agxmeister.Uplink.Port";

        public int Port
        {
            get { return EditorPrefs.GetInt(PortKey, DefaultPort); }
            set { EditorPrefs.SetInt(PortKey, IsValidPort(value) ? value : DefaultPort); }
        }

        public static bool IsValidPort(int port)
        {
            return port > 0 && port <= 65535;
        }
    }
}
