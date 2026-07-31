namespace Agxmeister.Uplink.Diagnostics
{
    /// <summary>
    /// Where Uplink reports about itself. An abstraction so that everything below the composition root stays
    /// free of <c>UnityEngine.Debug</c> and can be tested without the Editor's log.
    /// </summary>
    public interface IUplinkLog
    {
        void Info(string message);

        void Warning(string message);

        void Error(string message);
    }
}
