using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Agxmeister.Uplink.Status
{
    /// <summary>The one place that reads Editor state from the UnityEditor APIs.</summary>
    public sealed class UnityEditorStatusProbe : IEditorStatusProbe
    {
        private readonly string uplinkVersion;

        public UnityEditorStatusProbe(string uplinkVersion)
        {
            this.uplinkVersion = uplinkVersion;
        }

        public EditorStatus Read()
        {
            var scene = SceneManager.GetActiveScene();

            var dirty = new List<string>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var open = SceneManager.GetSceneAt(i);
                if (open.isLoaded && open.isDirty)
                {
                    dirty.Add(string.IsNullOrEmpty(open.path) ? open.name : open.path);
                }
            }

            return new EditorStatus
            {
                UplinkVersion = uplinkVersion,
                UnityVersion = Application.unityVersion,
                Platform = Application.platform.ToString(),
                ProjectName = Application.productName,
                ProjectPath = Application.dataPath,
                ActiveBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                // An unsaved scene has no path, in which case its name is the only thing to report.
                ActiveScene = string.IsNullOrEmpty(scene.path) ? scene.name : scene.path,
                SceneDirty = scene.isDirty,
                DirtyScenes = dirty,
                IsPlaying = EditorApplication.isPlaying,
                IsPaused = EditorApplication.isPaused,
                IsCompiling = EditorApplication.isCompiling,
                IsUpdating = EditorApplication.isUpdating,
            };
        }
    }
}
