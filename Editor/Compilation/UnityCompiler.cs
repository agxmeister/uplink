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
    /// </summary>
    public sealed class UnityCompiler : IUplinkService, ICompiler
    {
        private const string StateKey = "compile";

        /// <summary>
        /// How long a requested compile may go without starting before it counts as "nothing to rebuild".
        /// Unity begins within a frame or two of a refresh when scripts have changed.
        /// </summary>
        private static readonly TimeSpan Grace = TimeSpan.FromSeconds(5);

        private readonly CompileLog log;
        private readonly ISessionStore store;

        private bool attached;
        private bool refreshPending;

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

        public CompileResult Poll()
        {
            var outcome = log.Advance(DateTime.UtcNow);
            Persist();

            // Refreshing here would reload the domain inside this call, closing the listener before the
            // answer could be written. Left for the next tick, so the client is told a compile has begun.
            refreshPending = refreshPending || outcome.ShouldTrigger;

            return outcome.Result;
        }

        /// <summary>
        /// A run left mid-flight by a reload nobody told us about would otherwise report `compiling` for the
        /// rest of the session. Whatever was recorded before it vanished is a better answer than none.
        /// </summary>
        private void Recover()
        {
            if (log.Phase == CompileLog.Compiling && !log.AwaitingStart && !EditorApplication.isCompiling)
            {
                log.Completed(DateTime.UtcNow);
                Persist();
            }
        }

        private void Tick()
        {
            if (refreshPending)
            {
                refreshPending = false;
                // Refresh rather than RequestScriptCompilation: it rebuilds only what changed, so calling the
                // tool again when nothing has been edited costs nothing.
                AssetDatabase.Refresh();
                return;
            }

            var busy = EditorApplication.isUpdating || EditorApplication.isCompiling;
            if (log.GaveUpWaiting(DateTime.UtcNow, Grace, busy))
            {
                log.Completed(DateTime.UtcNow);
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
            log.Completed(DateTime.UtcNow);
            Persist();
        }

        private void Persist()
        {
            Stored.Write(store, StateKey, log.Capture());
        }
    }
}
