using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Agxmeister.Uplink.Hierarchy
{
    /// <summary>
    /// The one place an object is named by the slash-separated path clients use. It sits apart from
    /// <see cref="UnitySceneProbe"/> because reading the hierarchy is no longer the only thing that needs to
    /// resolve a path — a framed screenshot does too.
    /// </summary>
    public static class ObjectPath
    {
        /// <summary>
        /// Walks to an object by path across every loaded scene. Deliberately not GameObject.Find, which
        /// cannot see inactive objects — and an object being inactive is often the thing being looked into.
        /// </summary>
        public static GameObject Find(string path)
        {
            var names = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (names.Length == 0)
            {
                return null;
            }

            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root.name != names[0])
                    {
                        continue;
                    }

                    var found = Descend(root, names);
                    if (found != null)
                    {
                        return found;
                    }
                }
            }

            return null;
        }

        public static GameObject Descend(GameObject root, string[] names)
        {
            var current = root.transform;
            for (var i = 1; i < names.Length; i++)
            {
                current = current.Find(names[i]);
                if (current == null)
                {
                    return null;
                }
            }
            return current.gameObject;
        }

        /// <summary>
        /// The slash-separated path from the scene root, which is what identifies an object to a client —
        /// instance ids mean nothing across a domain reload.
        /// </summary>
        public static string Of(GameObject subject)
        {
            var path = "/" + subject.name;
            var parent = subject.transform.parent;
            while (parent != null)
            {
                path = "/" + parent.name + path;
                parent = parent.parent;
            }
            return path;
        }
    }
}
