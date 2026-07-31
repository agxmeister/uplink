using Agxmeister.Uplink.Configuration;
using UnityEditor;
using UnityEngine;

namespace Agxmeister.Uplink
{
    /// <summary>
    /// `Window → Uplink`: shows whether the Editor is reachable and lets the port be changed.
    /// </summary>
    internal sealed class UplinkWindow : EditorWindow
    {
        [MenuItem("Window/Uplink")]
        private static void Open()
        {
            GetWindow<UplinkWindow>("Uplink").minSize = new Vector2(320f, 140f);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Status", Uplink.IsRunning ? "Serving" : "Stopped");
            EditorGUILayout.LabelField("API", Uplink.BaseUrl);
            EditorGUILayout.LabelField("OpenAPI", Uplink.BaseUrl + "/openapi.json");

            if (!string.IsNullOrEmpty(Uplink.LastError))
            {
                EditorGUILayout.HelpBox(Uplink.LastError, MessageType.Error);
            }

            EditorGUI.BeginChangeCheck();
            // Delayed: a plain IntField reports every keystroke, so typing "8788" would try to bind 8, 87, 878.
            var port = EditorGUILayout.DelayedIntField("Port", Uplink.Port);
            if (EditorGUI.EndChangeCheck() && port != Uplink.Port)
            {
                if (EditorPrefsSettings.IsValidPort(port))
                {
                    Uplink.Port = port;
                    Uplink.Restart();
                }
                else
                {
                    Debug.LogWarning(string.Format(
                        "[Uplink] {0} is not a usable port; keeping {1}.", port, Uplink.Port));
                }
            }

            EditorGUILayout.Space();
            if (GUILayout.Button(Uplink.IsRunning ? "Restart" : "Start"))
            {
                Uplink.Restart();
            }
        }

        private void OnInspectorUpdate()
        {
            Repaint();
        }
    }
}
