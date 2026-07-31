using System;

namespace Agxmeister.Uplink.Threading
{
    /// <summary>
    /// Moves work onto the Editor main thread, which is the only place UnityEditor APIs are legal, and blocks
    /// the caller until it is done.
    /// </summary>
    public interface IMainThreadDispatcher
    {
        /// <summary>
        /// Runs <paramref name="work"/> on the main thread and returns its result.
        /// </summary>
        /// <exception cref="TimeoutException">The main thread did not run the work within the timeout.</exception>
        T Run<T>(Func<T> work, TimeSpan timeout);
    }
}
