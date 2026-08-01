using Newtonsoft.Json;

namespace Agxmeister.Uplink.Persistence
{
    /// <summary>
    /// Reads and writes objects through an <see cref="ISessionStore"/>, which holds strings only.
    /// </summary>
    public static class Stored
    {
        public static T Read<T>(ISessionStore store, string key) where T : class
        {
            var json = store.Get(key);
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<T>(json);
            }
            catch (JsonException)
            {
                // Left over from an older version of the package, or half-written. Nothing stored here is
                // precious enough to fail a request over — treat it as absent.
                return null;
            }
        }

        public static void Write(ISessionStore store, string key, object value)
        {
            if (value == null)
            {
                store.Remove(key);
                return;
            }
            store.Set(key, JsonConvert.SerializeObject(value));
        }
    }
}
