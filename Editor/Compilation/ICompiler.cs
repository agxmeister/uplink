namespace Agxmeister.Uplink.Compilation
{
    /// <summary>
    /// Builds the project's scripts and says what the compiler thought of them. One method, because the tool
    /// is one call made repeatedly rather than a start and a separate poll — see <see cref="CompileLog"/>.
    /// </summary>
    public interface ICompiler
    {
        /// <summary>
        /// Starts a compile if none is under way, and reports where things stand. Called again while a run is
        /// going it changes nothing; called after one finished it hands over the outcome, once.
        /// </summary>
        CompileResult Poll();
    }
}
