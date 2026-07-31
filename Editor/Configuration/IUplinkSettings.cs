namespace Agxmeister.Uplink.Configuration
{
    /// <summary>Where Uplink's settings live, so nothing else needs to know it is EditorPrefs.</summary>
    public interface IUplinkSettings
    {
        /// <summary>The loopback port the API is served on.</summary>
        int Port { get; set; }
    }
}
