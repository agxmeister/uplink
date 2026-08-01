namespace Agxmeister.Uplink.Services
{
    /// <summary>
    /// A component that runs outside any request. Endpoints only ever answer; something has to be listening
    /// to the Editor *before* a request arrives — the console buffer, the compile watcher — and that is a
    /// service.
    ///
    /// <see cref="Uplink"/> attaches every service when the domain loads and detaches it before the domain
    /// goes away, which is also a service's chance to hand its state to an
    /// <see cref="Agxmeister.Uplink.Persistence.ISessionStore"/> and pick it up again on the other side.
    /// </summary>
    public interface IUplinkService
    {
        /// <summary>Subscribe to the Editor and restore anything kept across the last reload.</summary>
        void Attach();

        /// <summary>Unsubscribe and persist anything that must survive the reload about to happen.</summary>
        void Detach();
    }
}
