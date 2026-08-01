using System.Collections.Generic;

namespace Agxmeister.Uplink.Console
{
    /// <summary>
    /// What the Editor's own Console window already holds. Uplink only starts hearing about messages once it
    /// has loaded, so this is how the buffer is seeded with everything that happened before that.
    /// </summary>
    public interface IConsoleHistory
    {
        /// <summary>
        /// The Console's current contents, oldest first, or null if they could not be read. Null is an
        /// expected answer, not a failure: reading them relies on Unity internals.
        /// </summary>
        IList<ConsoleEntry> Read();
    }
}
