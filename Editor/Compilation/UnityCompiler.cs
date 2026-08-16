using System;
using System.IO;
using Agxmeister.Uplink.Persistence;
using Agxmeister.Uplink.Services;
using UnityEditor;
using UnityEditor.Compilation;

namespace Agxmeister.Uplink.Compilation
{
    /// <summary>
    /// The one place that drives Unity's compiler and listens to what it says.
    ///
    /// It is a service because the interesting part happens while nobody is asking: a compile reloads the
    /// domain, taking the listener and every static with it, so the messages have to be written to the
    /// session store as they arrive rather than collected when a request finally comes back.
    ///
    /// A run does not end at the compiler's last word. A successful build is followed by a domain reload, and
    /// the reload is the half a client actually waits for — it is what re-runs `[InitializeOnLoadMethod]`
    /// setup code. So the run is closed on the far side of the reload, a couple of quiet ticks in, once that
    /// code has had its say.
    /// </summary>
    public sealed class UnityCompiler : IUplinkService, ICompiler
    {
        private const string StateKey = "compile";

        /// <summary>
        /// How long a requested compile may go without starting before it counts as "nothing to rebuild".
        /// Unity begins within a frame or two of a refresh when scripts have changed.
        /// </summary>
        private static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

        /// <summary>
        /// How long a promised reload may fail to arrive before the run stops waiting for it. Generous,
        /// because a reload that is coming at all comes within a frame or two — this only fires when the
        /// Editor decided not to reload after all, and `compiling` forever would be worse than a late answer.
        /// </summary>
        private static readonly TimeSpan ReloadGrace = TimeSpan.FromSeconds(15);

        /// <summary>
        /// How many quiet ticks to let pass after a reload before handing the outcome over, so that work the
        /// reload deferred — `EditorApplication.delayCall`, first-update stages — gets to log first.
        /// </summary>
        private const int SettleTicks = 2;

        private readonly CompileLog log;
        private readonly ISessionStore store;

        private bool attached;
        private bool refreshPending;
        private bool forcePending;
        private int settling;

        public UnityCompiler(CompileLog log, ISessionStore store)
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

        public void Attach()
        {
            log.Restore(Stored.Read<CompileState>(store, StateKey));
            Recover();

            CompilationPipeline.compilationStarted += OnStarted;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyFinished;
            CompilationPipeline.compilationFinished += OnFinished;
            EditorApplication.update += Tick;
            attached = true;
        }

        public void Detach()
        {
            if (attached)
            {
                CompilationPipeline.compilationStarted -= OnStarted;
                CompilationPipeline.assemblyCompilationFinished -= OnAssemblyFinished;
                CompilationPipeline.compilationFinished -= OnFinished;
                EditorApplication.update -= Tick;
                attached = false;
            }

            Persist();
        }

        public CompileResult Poll(bool force)
        {
            var outcome = log.Advance(DateTime.UtcNow, force);
            Persist();

            // Refreshing here would reload the domain inside this call, closing the listener before the
            // answer could be written. Left for the next tick, so the client is told a compile has begun.
            refreshPending = refreshPending || outcome.ShouldTrigger;
            forcePending = forcePending || (outcome.ShouldTrigger && force);

            outcome.Result.IsPlaying = EditorApplication.isPlaying;
            return outcome.Result;
        }

        public CompileResult Peek()
        {
            // Nothing is persisted: the cycle was not touched, so what is stored still describes it.
            var result = log.Observe();
            result.IsPlaying = EditorApplication.isPlaying;
            return result;
        }

        /// <summary>
        /// Being here at all proves the last domain reload is over, so a run that crossed it — waiting for
        /// its promised reload, or cut off mid-build — is ready to be closed. Not immediately, though: the
        /// setup code the reload re-runs may still be logging, so the outcome is handed over after a couple
        /// of quiet ticks instead. Until then the endpoint keeps answering 202, which a polling client takes
        /// in stride, where done-before-the-logs would mislead it.
        /// </summary>
        private void Recover()
        {
            if (log.CrossedReload && !EditorApplication.isCompiling)
            {
                settling = SettleTicks;
            }
        }

        private void Tick()
        {
            if (refreshPending)
            {
                refreshPending = false;
                var forced = forcePending;
                forcePending = false;

                // Refresh rather than RequestScriptCompilation: it rebuilds only what changed, so calling the
                // tool again when nothing has been edited costs nothing.
                AssetDatabase.Refresh();

                if (forced)
                {
                    // Refresh alone will not reload when nothing changed, and the reload is the point of
                    // `force`. Asking for both is safe — Unity folds the request into the reload a build
                    // causes anyway when something did change.
                    log.ExpectReload(DateTime.UtcNow);
                    Persist();
                    EditorUtility.RequestScriptReload();
                }
                return;
            }

            var busy = EditorApplication.isUpdating || EditorApplication.isCompiling;

            if (settling > 0)
            {
                if (busy)
                {
                    return;
                }
                settling--;
                if (settling == 0)
                {
                    log.Reloaded(DateTime.UtcNow, EditorApplication.isPlayingOrWillChangePlaymode);
                    Persist();
                }
                return;
            }

            if (log.GaveUpWaiting(DateTime.UtcNow, Grace, busy))
            {
                log.Completed(DateTime.UtcNow);
                Persist();
            }

            if (log.GaveUpOnReload(DateTime.UtcNow, ReloadGrace, busy))
            {
                log.Reloaded(DateTime.UtcNow, EditorApplication.isPlayingOrWillChangePlaymode);
                Persist();
            }
        }

        private void OnStarted(object context)
        {
            log.Started(DateTime.UtcNow);
            Persist();
        }

        private void OnAssemblyFinished(string assemblyPath, CompilerMessage[] messages)
        {
            var assembly = Path.GetFileNameWithoutExtension(assemblyPath);
            foreach (var message in messages)
            {
                if (message.type != CompilerMessageType.Error && message.type != CompilerMessageType.Warning)
                {
                    continue;
                }

                log.Add(new CompileMessage
                {
                    File = message.file,
                    Line = message.line,
                    Column = message.column,
                    Message = message.message,
                    Assembly = assembly,
                    Level = message.type == CompilerMessageType.Error
                        ? CompileLevel.Error
                        : CompileLevel.Warning,
                });
            }

            // Written per assembly rather than at the end: the domain reload that follows a successful build
            // is exactly the moment this would otherwise be lost.
            Persist();
        }

        private void OnFinished(object context)
        {
            if (log.HasErrors)
            {
                // Errors mean the old assemblies stay and no reload follows: this is the whole outcome.
                log.Completed(DateTime.UtcNow);
            }
            else
            {
                // A successful build reloads the domain, and what the reload logs is part of the answer, so
                // the run stays open. `Recover` on the far side is what closes it.
                log.ExpectReload(DateTime.UtcNow);
            }
            Persist();
        }

        private void Persist()
        {
            Stored.Write(store, StateKey, log.Capture());
        }
    }
}
