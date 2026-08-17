using System.Collections.Generic;

namespace Agxmeister.Uplink.Controls
{
    /// <summary>
    /// Plays a script of input into the running game. Like <see cref="Compilation.ICompiler"/> the work is
    /// one call made repeatedly rather than a start and a separate poll, so there is one method that acts and
    /// beside it one that only looks.
    ///
    /// The implementation lives in an assembly that is not compiled at all when the Input System package is
    /// absent (ADR-0015). Everything on this side of the seam — the cycle, the schedule, the payload — is in
    /// the main assembly and is tested against a stub.
    /// </summary>
    public interface IInputDriver
    {
        /// <summary>
        /// Starts a script if none is playing, and reports where things stand. Called again while one plays
        /// it changes nothing; called after one finished it hands the outcome over, once.
        /// <paramref name="steps"/> is read only when a script is actually started.
        /// Throws <see cref="Http.BadRequestException"/> when the Editor is not in play mode, when a control
        /// name is not one the Input System knows, or when a step is malformed.
        /// </summary>
        InputResult Poll(IList<InputStep> steps);

        /// <summary>
        /// Reports where things stand and changes nothing: no script is started, and a result waiting to be
        /// handed over stays waiting. Safe to call any number of times, in or out of play mode.
        /// </summary>
        InputResult Peek();
    }
}
