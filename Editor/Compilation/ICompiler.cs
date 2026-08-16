namespace Agxmeister.Uplink.Compilation
{
    /// <summary>
    /// Builds the project's scripts and says what the compiler thought of them. The work is one call made
    /// repeatedly rather than a start and a separate poll — see <see cref="CompileLog"/> — so there is one
    /// method that acts, and beside it one that only looks, for a client that wants to know without risking
    /// a build it did not ask for.
    /// </summary>
    public interface ICompiler
    {
        /// <summary>
        /// Starts a compile if none is under way, and reports where things stand. Called again while a run is
        /// going it changes nothing; called after one finished it hands over the outcome, once.
        /// <paramref name="force"/> makes the run it starts reload the domain even when no script changed.
        /// </summary>
        CompileResult Poll(bool force);

        /// <summary>
        /// Reports where things stand and changes nothing at all: no compile is started, and a result waiting
        /// to be handed over by <see cref="Poll"/> stays waiting. Safe to call any number of times.
        /// </summary>
        CompileResult Peek();
    }
}
