using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace altinnendata_api.Helpers
{
    public static partial class Slugify
    {
        public static string Create(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var normalized = input
                .Replace("æ", "ae").Replace("Æ", "ae")
                .Replace("ø", "o").Replace("Ø", "o")
                .Replace("å", "a").Replace("Å", "a");

            normalized = normalized.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }

            var slug = sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
            slug = NonSlugChars().Replace(slug, "-");
            slug = MultiDash().Replace(slug, "-").Trim('-');
            return slug;
        }

        [GeneratedRegex("[^a-z0-9]+")]
        private static partial Regex NonSlugChars();

        [GeneratedRegex("-{2,}")]
        private static partial Regex MultiDash();
    }
}
