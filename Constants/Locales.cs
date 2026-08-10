namespace altinnendata_api.Constants
{
    public static class Locales
    {
        public const string Default = "no";

        public static readonly string[] Supported = ["no", "en"];

        public static bool IsSupported(string? locale) =>
            locale != null && Supported.Contains(locale, StringComparer.OrdinalIgnoreCase);

        /// <summary>Normalises "nb-NO", "NO", null or an unknown tag to a supported locale.</summary>
        public static string Normalize(string? locale)
        {
            if (string.IsNullOrWhiteSpace(locale)) return Default;

            var tag = locale.Trim();
            var match = Supported.FirstOrDefault(l => string.Equals(l, tag, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match.ToLowerInvariant();

            var primary = tag.Split('-')[0];
            if (string.Equals(primary, "nb", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(primary, "nn", StringComparison.OrdinalIgnoreCase))
                return "no";

            return Supported.FirstOrDefault(l => string.Equals(l, primary, StringComparison.OrdinalIgnoreCase))?.ToLowerInvariant()
                ?? Default;
        }
    }
}
