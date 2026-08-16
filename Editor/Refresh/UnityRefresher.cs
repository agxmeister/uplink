using System;
using System.Collections.Generic;
using Agxmeister.Uplink.Persistence;
using Agxmeister.Uplink.Services;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Agxmeister.Uplink.Refresh
{
    /// <summary>
    /// The one place that makes the Editor re-read the project from disk.
    ///
    /// Two separate mechanisms are involved, and only the first is what people expect. Importing picks up
    /// changed files, but it does not touch a scene the Editor already has open: Unity keeps its in-memory
    /// copy and never re-reads that file, so an edit written into a `.unity` is invisible until the scene is
    /// re-opened. Both are done here, in that order.
    ///
    /// It is a service because importing can reload the domain, taking the listener and every static with it,
    /// so the outcome has to survive in the session store rather than be collected when a request returns.
    /// </summary>
    public sealed class UnityRefresher : IUplinkService, IRefresher
    {
        private const string StateKey = "refresh";

        private readonly RefreshLog log;
        private readonly ISessionStore store;

        private bool attached;
        private bool refreshPending;

        public UnityRefresher(RefreshLog log, ISessionStore store)
        {
            if (log == null)
            {
                throw new ArgumentNullException("log");
            }
            if (store == null)
            {
                throw new ArgumentNullException("store");
            }

            this.log = log;
            this.store = store;
        }

        public bool IsPlaying
        {
            get { return EditorApplication.isPlayingOrWillChangePlaymode; }
        }

        public void Attach()
        {
            log.Restore(Stored.Read<RefreshState>(store, StateKey));
            Recover();

            EditorApplication.update += Tick;
            attached = true;
        }

        public void Detach()
        {
            if (attached)
            {
                EditorApplication.update -= Tick;
                attached = false;
            }

            Persist();
        }

        public IList<OpenScene> OpenScenes()
        {
            var scenes = new List<OpenScene>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                {
                    scenes.Add(Describe(scene));
                }
            }
            return scenes;
        }

        public RefreshResult Poll(bool scenes)
        {
            var outcome = log.Advance(DateTime.UtcNow, scenes);
            Persist();

            // Importing here would reload the domain inside this call, closing the listener before the answer
            // could be written. Left for the next tick, so the client is told a refresh has begun.
            refreshPending = refreshPending || outcome.ShouldTrigger;

            outcome.Result.IsPlaying = IsPlaying;
            return outcome.Result;
        }

        /// <summary>
        /// A run left mid-flight by a reload nobody told us about would otherwise report `refreshing` for the
        /// rest of the session. Whatever the Editor holds now is a better answer than none.
        /// </summary>
        private void Recover()
        {
            if (log.Phase == RefreshLog.Refreshing && !EditorApplication.isUpdating)
            {
                // The import got as far as reloading the domain, so it did happen; the scenes did not, and
                // saying so is better than claiming a reload that never ran.
                log.Completed(DateTime.UtcNow, false, OpenScenes());
                Persist();
            }
        }

        private void Tick()
        {
            if (!refreshPending)
            {
                return;
            }
            refreshPending = false;

            var scenes = log.WantsScenes;

            // Import first: a scene re-opened before its new dependencies exist would lose the references.
            AssetDatabase.Refresh();

            // Only reached when importing did not reload the domain. When it did, `Recover` reports instead.
            var open = scenes ? ReloadScenes() : OpenScenes();
            log.Completed(DateTime.UtcNow, scenes, open);
            Persist();
        }

        /// <summary>
        /// Re-opens the open scenes from their files. <c>OpenScene</c> discards unsaved work silently rather
        /// than asking — the Editor UI only prompts because it calls
        /// <c>SaveCurrentModifiedScenesIfUserWantsTo</c> first, which this must never do: a modal dialog in a
        /// headless call hangs the Editor with nobody there to dismiss it. Guarding the caller's unsaved work
        /// is therefore the endpoint's job, not Unity's.
        /// </summary>
        private IList<OpenScene> ReloadScenes()
        {
            var paths = new List<string>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                // A scene never saved has no file to re-read, and re-opening it would only lose it.
                if (!scene.isLoaded || string.IsNullOrEmpty(scene.path))
                {
                    continue;
                }

                paths.Add(scene.path);
            }

            var activePath = SceneManager.GetActiveScene().path;
            var reloaded = new List<OpenScene>();

            for (var i = 0; i < paths.Count; i++)
            {
                // The first replaces the open set; the rest rebuild the additive arrangement around it.
                var mode = i == 0 ? OpenSceneMode.Single : OpenSceneMode.Additive;
                var scene = EditorSceneManager.OpenScene(paths[i], mode);
                reloaded.Add(Describe(scene));

                if (scene.path == activePath)
                {
                    SceneManager.SetActiveScene(scene);
                }
            }

            return reloaded;
        }

        private static OpenScene Describe(Scene scene)
        {
            return new OpenScene
            {
                Name = scene.name,
                Path = scene.path,
                IsDirty = scene.isDirty,
                RootCount = scene.rootCount,
            };
        }

        private void Persist()
        {
            Stored.Write(store, StateKey, log.Capture());
        }
    }
}
