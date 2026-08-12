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

        public static ParsedFinnAd Parse(string html)
        {
            var title = Meta(html, "og:title") ?? TitleTag(html);
            var description = Meta(html, "og:description");
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

            var unique = images
                .Select(WebUtility.HtmlDecode)
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Select(url => url!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxImages)
                .ToList();

            return new ParsedFinnAd(
                WebUtility.HtmlDecode(title)?.Trim(),
                WebUtility.HtmlDecode(description)?.Trim(),
                price,
                unique);
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

        private static int? PriceFromMarkup(string html)
        {
            var match = Regex.Match(html, "\"price\"\\s*:\\s*\"?(\\d{3,9})", RegexOptions.IgnoreCase);
            return match.Success && int.TryParse(match.Groups[1].Value, out var price) ? price : null;
        }
    }
}
