namespace Agxmeister.Uplink.Http
{
    /// <summary>
    /// The single place that decides when two paths are the same path, so that the router and the OpenAPI
    /// document can never disagree about it.
    /// </summary>
    public static class Route
    {
        public static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "/";
            }

            if (path[0] != '/')
            {
                path = "/" + path;
            }

            var trimmed = path.TrimEnd('/');
            return trimmed.Length == 0 ? "/" : trimmed;
        }

        public static bool Matches(string endpointPath, string requestPath)
        {
            return string.Equals(Normalize(endpointPath), Normalize(requestPath), System.StringComparison.Ordinal);
        }
    }
}
