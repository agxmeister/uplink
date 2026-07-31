using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace Agxmeister.Uplink.Threading
{
    /// <summary>
    /// A queue drained by whoever owns the main thread: <see cref="Pump"/> is expected to be called from
    /// <c>EditorApplication.update</c>. Deliberately free of any Unity dependency so it can be pumped by hand
    /// in tests.
    /// </summary>
    public sealed class MainThreadDispatcher : IMainThreadDispatcher
    {
        private readonly Queue<WorkItem> pending = new Queue<WorkItem>();

        public T Run<T>(Func<T> work, TimeSpan timeout)
        {
            if (work == null)
            {
                throw new ArgumentNullException("work");
            }

            var item = new WorkItem(() => work());
            lock (pending)
            {
                pending.Enqueue(item);
            }

            return (T)item.Await(timeout);
        }

        /// <summary>Runs everything queued so far. Must be called on the main thread.</summary>
        public void Pump()
        {
            while (true)
            {
                WorkItem item;
                lock (pending)
                {
                    if (pending.Count == 0)
                    {
                        return;
                    }
                    item = pending.Dequeue();
                }
                item.Execute();
            }
        }

        /// <summary>
        /// One queued call. Abandoning it on timeout matters: without that, work whose caller has already
        /// given up still runs later against the Editor, at an arbitrary moment.
        /// </summary>
        private sealed class WorkItem
        {
            private readonly Func<object> work;
            private readonly ManualResetEventSlim completed = new ManualResetEventSlim(false);
            private readonly object gate = new object();

            private object result;
            private Exception failure;
            private bool abandoned;

            public WorkItem(Func<object> work)
            {
                this.work = work;
            }

            public void Execute()
            {
                lock (gate)
                {
                    if (abandoned)
                    {
                        return;
                    }
                }

                object value = null;
                Exception error = null;
                try
                {
                    value = work();
                }
                catch (Exception exception)
                {
                    error = exception;
                }

                lock (gate)
                {
                    result = value;
                    failure = error;
                }
                completed.Set();
            }

            public object Await(TimeSpan timeout)
            {
                if (!completed.Wait(timeout))
                {
                    lock (gate)
                    {
                        abandoned = true;
                    }
                    throw new TimeoutException(string.Format(
                        "The Editor main thread did not respond within {0}. It is most likely compiling, " +
                        "importing assets, or blocked by a modal dialog.", timeout));
                }

                lock (gate)
                {
                    if (failure != null)
                    {
                        // Rethrow without flattening the original stack trace.
                        ExceptionDispatchInfo.Capture(failure).Throw();
                    }
                    return result;
                }
            }
        }
    }
}
