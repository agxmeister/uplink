using Agxmeister.Uplink.Persistence;
using UnityEditor;

namespace Agxmeister.Uplink.Controls
{
    /// <summary>
    /// The composition root for a capability the real one could not name.
    ///
    /// This assembly is compiled only when the Input System package is present (see the asmdef's
    /// `versionDefines` and `defineConstraints`), so <see cref="Uplink"/> cannot mention any type in it
    /// without failing to compile in every project that lacks the package. The dependency therefore inverts:
    /// the capability introduces itself. ADR-0015 records why, and why it is safe — the endpoint collection
    /// is read per request, so a late arrival shows up in the live routes and in `/openapi.json` at once.
    ///
    /// Unity runs `[InitializeOnLoad]` static constructors before `[InitializeOnLoadMethod]` methods, and
    /// touching <see cref="Uplink.Register"/> would trigger Uplink's anyway, so the ordering here is not
    /// luck.
    /// </summary>
    public static class Registration
    {
        [InitializeOnLoadMethod]
        private static void Announce()
        {
            var driver = new UnityInputDriver(new InputScript(), new SessionStateStore());

            // One call, both endpoints: registering twice would attach the service twice, and a cycle's
            // acting verb and its read-only twin belong to the same driver.
            Uplink.Register(driver, new InputEndpoint(driver), new InputStatusEndpoint(driver));
        }
    }
}
