namespace altinnendata_api.Features.Finn
{
    /// <summary>
    /// Host allowlist for everything the import fetches. Without it the endpoint would fetch any
    /// address an admin pasted, including addresses inside the cluster.
    /// </summary>
    public static class FinnUrls
    {
        private static readonly string[] AdHosts = ["finn.no"];
        private static readonly string[] ImageHosts = ["finncdn.no", "finn.no"];

        public static bool IsAdUrl(string? url) => IsAllowed(url, AdHosts);

        public static bool IsImageUrl(string? url) => IsAllowed(url, ImageHosts);

        private static bool IsAllowed(string? url, string[] hosts) =>
            Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            && parsed.Scheme == Uri.UriSchemeHttps
            && hosts.Any(host =>
                parsed.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
                || parsed.Host.EndsWith($".{host}", StringComparison.OrdinalIgnoreCase));
    }
}
