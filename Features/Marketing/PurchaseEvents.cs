using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using SjolystInnovation.AppKit.Vipps;
using altinnendata_api.Models.Shop;
using altinnendata_api.Services;

namespace altinnendata_api.Features.Marketing
{
    /// <summary>
    /// Builds the server-side Purchase event of an order for the Meta Conversions API and the TikTok Events API.
    /// The event id is the order reference, the same id the browser pixels send, so each platform keeps one of the two.
    /// </summary>
    public static class PurchaseEvents
    {
        public const string EventName = "Purchase";
        public const string Currency = "NOK";
        public const string Country = "no";

        /// <summary>SHA-256 of the UTF-8 bytes of <paramref name="value"/>, in lowercase hex.</summary>
        public static string Sha256(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

        /// <summary>The email trimmed and lowercased, or null when empty.</summary>
        public static string? NormalizeEmail(string? email)
        {
            var e = email?.Trim().ToLowerInvariant();
            return string.IsNullOrEmpty(e) ? null : e;
        }

        /// <summary>The phone as <c>47</c> and 8 digits, or null when it is not a Norwegian number.</summary>
        public static string? NormalizePhone(string? phone) =>
            VippsPhoneNumber.TryNormalize(phone, out var msisdn) ? msisdn : null;

        /// <summary>The postcode lowercased without spaces or dashes, or null when empty.</summary>
        public static string? NormalizeZip(string? zip)
        {
            var z = new string((zip ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToLowerInvariant();
            return z.Length == 0 ? null : z;
        }

        /// <summary>The city lowercased with only its letters and digits, or null when empty.</summary>
        public static string? NormalizeCity(string? city)
        {
            var c = new string((city ?? "").Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            return c.Length == 0 ? null : c;
        }

        /// <summary>
        /// The city for TikTok, which asks for it without special characters: lowercase, with æ, ø and å folded to
        /// ae, o and a, and only the ASCII letters and digits kept. Null when nothing is left.
        /// </summary>
        public static string? NormalizeTikTokCity(string? city)
        {
            var folded = (city ?? "").ToLowerInvariant().Replace("æ", "ae").Replace("ø", "o").Replace("å", "a");
            var c = new string(folded.Where(char.IsAsciiLetterOrDigit).ToArray());
            return c.Length == 0 ? null : c;
        }

        /// <summary>Øre as kroner.</summary>
        public static decimal Kroner(int ore) => ore / 100m;

        /// <summary>The page the event happened on: the checkout page sent by the site, or the build's page.</summary>
        public static string SourceUrl(Order order, IConfiguration config) =>
            order.EventSourceUrl ?? SiteLinks.Build(config, order.BuildSlug);

        private static long UnixSeconds(DateTime at) =>
            new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)).ToUnixTimeSeconds();

        private static void AddIf(JsonObject target, string name, JsonNode? value)
        {
            if (value != null) target[name] = value;
        }

        private static JsonArray? HashedList(string? normalized) => normalized == null ? null : new JsonArray(Sha256(normalized));

        private static JsonNode? Hashed(string? normalized) => normalized == null ? null : JsonValue.Create(Sha256(normalized));

        private static JsonNode? Plain(string? value) => string.IsNullOrEmpty(value) ? null : JsonValue.Create(value);

        /// <summary>The body of <c>POST /{pixel_id}/events</c> for the Meta Conversions API.</summary>
        public static JsonObject MetaBody(Order order, IConfiguration config, string? testEventCode)
        {
            var user = new JsonObject();
            AddIf(user, "em", HashedList(NormalizeEmail(order.BuyerEmail)));
            AddIf(user, "ph", HashedList(NormalizePhone(order.BuyerPhone)));
            AddIf(user, "zp", HashedList(NormalizeZip(order.PostalCode)));
            AddIf(user, "ct", HashedList(NormalizeCity(order.City)));
            user["country"] = HashedList(Country);
            AddIf(user, "client_ip_address", Plain(order.ClientIp));
            AddIf(user, "client_user_agent", Plain(order.ClientUserAgent));
            AddIf(user, "fbp", Plain(order.Fbp));
            AddIf(user, "fbc", Plain(order.Fbc));

            // The Conversions API docs at hand show id and quantity in contents. item_price is left out because they do not show it.
            var custom = new JsonObject
            {
                ["value"] = Kroner(order.TotalInOre),
                ["currency"] = Currency,
                ["content_ids"] = new JsonArray(order.BuildSlug),
                ["content_type"] = "product",
                ["contents"] = new JsonArray(new JsonObject { ["id"] = order.BuildSlug, ["quantity"] = 1 })
            };

            var data = new JsonObject
            {
                ["event_name"] = EventName,
                ["event_time"] = UnixSeconds(order.AuthorizedAt!.Value),
                ["action_source"] = "website",
                ["event_source_url"] = SourceUrl(order, config),
                ["event_id"] = order.Reference,
                ["user_data"] = user,
                ["custom_data"] = custom
            };

            var body = new JsonObject { ["data"] = new JsonArray(data) };
            AddIf(body, "test_event_code", Plain(testEventCode));
            return body;
        }

        /// <summary>The body of <c>POST /event/track/</c> for the TikTok Events API 2.0.</summary>
        public static JsonObject TikTokBody(Order order, string pixelCode, IConfiguration config, string? testEventCode)
        {
            var phone = NormalizePhone(order.BuyerPhone);
            var user = new JsonObject();
            AddIf(user, "email", Hashed(NormalizeEmail(order.BuyerEmail)));
            AddIf(user, "phone", Hashed(phone == null ? null : "+" + phone));
            AddIf(user, "zip_code", Hashed(NormalizeZip(order.PostalCode)));
            AddIf(user, "city", Plain(NormalizeTikTokCity(order.City)));
            user["country"] = Country;
            AddIf(user, "ip", Plain(order.ClientIp));
            AddIf(user, "user_agent", Plain(order.ClientUserAgent));
            AddIf(user, "ttp", Plain(order.Ttp));
            AddIf(user, "ttclid", Plain(order.Ttclid));

            var properties = new JsonObject
            {
                ["contents"] = new JsonArray(new JsonObject
                {
                    ["content_id"] = order.BuildSlug,
                    ["quantity"] = 1,
                    ["price"] = Kroner(order.BuildPriceInOre)
                }),
                ["content_type"] = "product",
                ["value"] = Kroner(order.TotalInOre),
                ["currency"] = Currency
            };

            var data = new JsonObject
            {
                ["event"] = EventName,
                ["event_time"] = UnixSeconds(order.AuthorizedAt!.Value),
                ["event_id"] = order.Reference,
                ["user"] = user,
                ["properties"] = properties,
                ["page"] = new JsonObject { ["url"] = SourceUrl(order, config) }
            };

            var body = new JsonObject
            {
                ["event_source"] = "web",
                ["event_source_id"] = pixelCode,
                ["data"] = new JsonArray(data)
            };
            AddIf(body, "test_event_code", Plain(testEventCode));
            return body;
        }
    }
}
