namespace Agxmeister.Uplink.Persistence
{
    /// <summary>
    /// Storage whose lifetime is one Editor session: it survives a domain reload but not a restart.
    ///
    /// That lifetime is the whole point. Compiling and entering play mode wipe every static and close the
    /// listener, so the request that starts such work can never be the request that reports it — the result
    /// has to outlive the reload, and it must not outlive the session that produced it.
    /// </summary>
    public interface ISessionStore
    {
        /// <summary>The stored value, or null when the key was never written or has been removed.</summary>
        string Get(string key);

        void Set(string key, string value);

        void Remove(string key);
    }
}
