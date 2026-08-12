using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace altinnendata_api.Features.Finn
{
    public record ParsedFinnAd(string? Title, string? Description, int? PriceNok, IReadOnlyList<string> ImageUrls);

    /// <summary>Reads the OpenGraph and JSON-LD blocks a finn.no advert exposes for link previews.</summary>
    public static partial class FinnAdParser
    {
        private const int MaxImages = 12;

        public static ParsedFinnAd Parse(string html, string? adUrl = null)
        {
            var title = CleanTitle(Text(Meta(html, "og:title") ?? TitleTag(html)));
            var description = BodyDescription(html) ?? Text(Meta(html, "og:description"));
            var images = new List<string>();

            var ogImage = Meta(html, "og:image");
            if (ogImage != null) images.Add(ogImage);

            int? price = null;

            foreach (var json in JsonLdBlocks(html))
            {
                try
                {
                    using var document = JsonDocument.Parse(json);
                    ReadJsonLd(document.RootElement, ref price, images);
                }
                catch (JsonException)
                {
                    // A malformed block is skipped; the OpenGraph tags still stand.
                }
            }

            price ??= PriceFromMarkup(html);

            images.AddRange(GalleryImages(html, adUrl));

            var unique = images
                .Select(WebUtility.HtmlDecode)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => url!.Trim())
                .Where(IsAdPhoto)
                .GroupBy(PhotoName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(Width).First())
                .Take(MaxImages)
                .ToList();

            return new ParsedFinnAd(title, description, price, unique);
        }

        private static void ReadJsonLd(JsonElement element, ref int? price, List<string> images)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        ReadJsonLd(item, ref price, images);
                    break;

                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.NameEquals("image"))
                            CollectImages(property.Value, images);
                        else if (property.NameEquals("price") && price == null)
                            price = AsInt(property.Value);
                        else
                            ReadJsonLd(property.Value, ref price, images);
                    }
                    break;
            }
        }

        private static void CollectImages(JsonElement element, List<string> images)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var url = element.GetString();
                if (url != null) images.Add(url);
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                    CollectImages(item, images);
            }
            else if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("url", out var url))
            {
                CollectImages(url, images);
            }
        }

        private static int? AsInt(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(
                new string(element.GetString()!.Where(char.IsDigit).ToArray()), out var parsed) => parsed,
            _ => null,
        };

        private static string? CleanTitle(string? title)
        {
            if (title == null) return null;

            var cleaned = Regex.Replace(title, @"\s*\|\s*FINN\b.*$", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s+[-–]\s+FINN\b.*$", "", RegexOptions.IgnoreCase);
            return cleaned.Trim();
        }

        /// <summary>
        /// The advert's own description block. The OpenGraph tag holds a shortened copy of it,
        /// so it is only a fallback.
        /// </summary>
        private static string? BodyDescription(string html)
        {
            var section = Section(html, "data-testid=[\"']description[\"']")
                ?? Section(html, "aria-labelledby=[\"']item-description-heading[\"']");
            if (section == null) return null;

            var text = Text(ToPlainText(section));
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }

        private static string? Section(string html, string attributePattern)
        {
            var open = Regex.Match(html, $"<section[^>]*{attributePattern}[^>]*>", RegexOptions.IgnoreCase);
            if (!open.Success) return null;

            var start = open.Index + open.Length;
            var depth = 1;

            foreach (Match tag in Regex.Matches(html[start..], "</?section\\b", RegexOptions.IgnoreCase))
            {
                depth += tag.Value.StartsWith("</", StringComparison.Ordinal) ? -1 : 1;
                if (depth == 0) return html.Substring(start, tag.Index);
            }

            return null;
        }

        private static string ToPlainText(string fragment)
        {
            var text = Regex.Replace(fragment, "<(script|style|button|w-button)\\b.*?</\\1>", " ",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, "<(\\w[\\w-]*)[^>]*class=[\"'][^\"']*sr-only[^\"']*[\"'][^>]*>.*?</\\1>", " ",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            text = Regex.Replace(text, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "</(p|div|li|ul|ol|h[1-6]|section|tr)>", "\n\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]+>", "");
            text = WebUtility.HtmlDecode(text).Replace("\r\n", "\n").Replace('\r', '\n');
            text = string.Join('\n', text.Split('\n').Select(line => line.Trim()));
            return Regex.Replace(text, "\n{3,}", "\n\n").Trim();
        }

        private static string? Text(string? value) => Unescape(WebUtility.HtmlDecode(value))?.Trim();

        /// <summary>finn.no writes emoji as \u{d83d}\u{dd0c} escape pairs rather than as characters.</summary>
        private static string? Unescape(string? text)
        {
            if (text == null || !text.Contains("\\u", StringComparison.Ordinal)) return text;

            return Regex.Replace(text, "\\\\u\\{([0-9a-fA-F]{1,6})\\}|\\\\u([0-9a-fA-F]{4})", match =>
            {
                var digits = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                var code = Convert.ToInt32(digits, 16);
                if (code > 0x10FFFF) return match.Value;
                return code <= 0xFFFF ? ((char)code).ToString() : char.ConvertFromUtf32(code);
            });
        }

        private static string? Meta(string html, string property)
        {
            var match = Regex.Match(
                html,
                $"<meta[^>]+(?:property|name)=[\"']{Regex.Escape(property)}[\"'][^>]*content=[\"']([^\"']*)[\"']",
                RegexOptions.IgnoreCase);
            if (match.Success) return match.Groups[1].Value;

            match = Regex.Match(
                html,
                $"<meta[^>]+content=[\"']([^\"']*)[\"'][^>]*(?:property|name)=[\"']{Regex.Escape(property)}[\"']",
                RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static string? TitleTag(string html)
        {
            var match = Regex.Match(html, "<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? match.Groups[1].Value : null;
        }

        private static IEnumerable<string> JsonLdBlocks(string html) =>
            Regex.Matches(html, "<script[^>]+application/ld\\+json[^>]*>(.*?)</script>",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline)
                .Select(m => m.Groups[1].Value);

        /// <summary>
        /// The gallery only appears as image tags, and every photo is served at several widths.
        /// Matching on the advert id keeps other adverts' thumbnails out.
        /// </summary>
        private static IEnumerable<string> GalleryImages(string html, string? adUrl)
        {
            var id = AdId(adUrl);
            if (id == null) return [];

            // Newer adverts serve /dynamic/{width}w/item/{id}/{uuid}, older ones
            // /dynamic/{width}w/{date path}/{id split in threes}_{uuid}.jpg.
            var flat = $"item/{id}";
            var split = SplitInThrees(id);

            return Regex.Matches(html, "https://images\\.finncdn\\.no/dynamic/\\d+w/[^\"'\\\\ );]+", RegexOptions.IgnoreCase)
                .Select(m => m.Value)
                .Where(url => url.Contains(flat, StringComparison.Ordinal) || url.Contains(split, StringComparison.Ordinal));
        }

        private static string? AdId(string? adUrl)
        {
            var id = adUrl == null ? "" : Regex.Match(adUrl, "(\\d{6,})").Value;
            return id.Length >= 6 ? id : null;
        }

        private static string SplitInThrees(string id)
        {
            var groups = new List<string>();
            for (var i = 0; i < id.Length; i += 3)
                groups.Add(id.Substring(i, Math.Min(3, id.Length - i)));
            return string.Join('/', groups);
        }

        /// <summary>Keeps FINN's own logos and placeholders, which the JSON-LD blocks also carry, out of the gallery.</summary>
        private static bool IsAdPhoto(string url) =>
            Regex.IsMatch(url, "^https://images\\.finncdn\\.no/dynamic/\\d+w/", RegexOptions.IgnoreCase);

        private static string PhotoName(string url) => url.Split('/').LastOrDefault() ?? url;

        private static int Width(string url)
        {
            var match = Regex.Match(url, "/dynamic/(\\d+)w/");
            return match.Success && int.TryParse(match.Groups[1].Value, out var width) ? width : 0;
        }

        private static int? PriceFromMarkup(string html)
        {
            var match = Regex.Match(html, "\"price\"\\s*:\\s*\"?(\\d{3,9})", RegexOptions.IgnoreCase);
            return match.Success && int.TryParse(match.Groups[1].Value, out var price) ? price : null;
        }
    }
}
