using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Agxmeister.Uplink.Hierarchy
{
    /// <summary>The one place that walks Unity's scene graph.</summary>
    public sealed class UnitySceneProbe : ISceneProbe
    {
        /// <summary>
        /// How many objects a single walk will describe. A production scene can hold tens of thousands, and
        /// an answer that large is no use to anyone — narrowing with `path` is.
        /// </summary>
        private const int MaxNodes = 2000;

        public SceneTree ReadTree(SceneQuery query)
        {
            var budget = MaxNodes;

            if (!string.IsNullOrEmpty(query.Path))
            {
                var found = Find(query.Path);
                if (found == null)
                {
                    return new SceneTree { Scenes = new List<SceneSummary>() };
                }

                return new SceneTree
                {
                    Scenes = new List<SceneSummary>
                    {
                        Summarize(found.scene, new List<SceneNode> { Walk(found, query, query.Depth, ref budget) }),
                    },
                    Truncated = budget <= 0,
                };
            }

            var scenes = new List<SceneSummary>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                var roots = new List<SceneNode>();

                if (scene.isLoaded)
                {
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        roots.Add(Walk(root, query, query.Depth, ref budget));
                    }
                }

                scenes.Add(Summarize(scene, roots));
            }

            return new SceneTree { Scenes = scenes, Truncated = budget <= 0 };
        }

        public ObjectDetail ReadObject(string path)
        {
            var found = Find(path);
            if (found == null)
            {
                return null;
            }

            var components = new List<ComponentDetail>();
            foreach (var component in found.GetComponents<Component>())
            {
                components.Add(Describe(component));
            }

            var children = new List<string>();
            foreach (Transform child in found.transform)
            {
                children.Add(child.gameObject.name);
            }

            return new ObjectDetail
            {
                Name = found.name,
                Path = PathOf(found),
                Scene = found.scene.name,
                Active = found.activeInHierarchy,
                Tag = found.tag,
                Layer = LayerMask.LayerToName(found.layer),
                Components = components,
                Children = children,
            };
        }

        private static SceneSummary Summarize(Scene scene, IList<SceneNode> roots)
        {
            return new SceneSummary
            {
                Name = scene.name,
                Path = scene.path,
                IsLoaded = scene.isLoaded,
                IsActive = scene == SceneManager.GetActiveScene(),
                Roots = roots,
            };
        }

        private static SceneNode Walk(GameObject subject, SceneQuery query, int depth, ref int budget)
        {
            budget--;

            var node = new SceneNode
            {
                Name = subject.name,
                Path = PathOf(subject),
                Active = subject.activeInHierarchy,
                Tag = subject.tag,
                Layer = LayerMask.LayerToName(subject.layer),
                ChildCount = subject.transform.childCount,
            };

            if (query.Components)
            {
                node.Components = ComponentNames(subject);
            }

            if (depth <= 0 || node.ChildCount == 0)
            {
                return node;
            }

            var children = new List<SceneNode>();
            foreach (Transform child in subject.transform)
            {
                if (budget <= 0)
                {
                    break;
                }
                children.Add(Walk(child.gameObject, query, depth - 1, ref budget));
            }

            // Left absent rather than empty when the walk stopped here, so that "no children" and "not looked
            // at" do not read the same. `childCount` says which happened.
            if (children.Count > 0)
            {
                node.Children = children;
            }

            return node;
        }

        private static IList<string> ComponentNames(GameObject subject)
        {
            var names = new List<string>();
            foreach (var component in subject.GetComponents<Component>())
            {
                // Null when the script behind it is missing, which is worth seeing rather than hiding.
                names.Add(component == null ? "Missing Script" : component.GetType().Name);
            }
            return names;
        }

        private static ComponentDetail Describe(Component component)
        {
            if (component == null)
            {
                return new ComponentDetail
                {
                    Type = "Missing Script",
                    Properties = new Dictionary<string, object>(),
                };
            }

            var behaviour = component as Behaviour;
            return new ComponentDetail
            {
                Type = component.GetType().Name,
                Enabled = behaviour == null ? (bool?)null : behaviour.enabled,
                Properties = SerializedValues.Of(component),
            };
        }

        /// <summary>
        /// The slash-separated path from the scene root, which is what identifies an object to a client —
        /// instance ids mean nothing across a domain reload.
        /// </summary>
        private static string PathOf(GameObject subject)
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

        /// <summary>
        /// Walks to an object by path across every loaded scene. Deliberately not GameObject.Find, which
        /// cannot see inactive objects — and an object being inactive is often the thing being looked into.
        /// </summary>
        private static GameObject Find(string path)
        {
            var names = path.Split(new[] { '/' }, System.StringSplitOptions.RemoveEmptyEntries);
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

        private static GameObject Descend(GameObject root, string[] names)
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
    }
}
