namespace altinnendata_api.Services
{
    /// <summary>Builds absolute links back into the website for emails.</summary>
    public static class SiteLinks
    {
        private const string Fallback = "https://www.altinnendata.no";
        private const string DefaultLocale = "no";

        public static string BaseUrl(IConfiguration config) =>
            (config["Site:BaseUrl"] ?? Fallback).TrimEnd('/');

        public static string SetPassword(IConfiguration config, string email, string code) =>
            $"{BaseUrl(config)}/{DefaultLocale}/reset-password?email={Uri.EscapeDataString(email)}&code={Uri.EscapeDataString(code)}";
    }
}
