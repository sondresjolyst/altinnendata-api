using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace altinnendata_api.Features.Marketing
{
    /// <summary>The outcome of one call: whether the platform accepted the event, its HTTP status and its error code.</summary>
    public readonly record struct PlatformResult(bool Accepted, int Status, long? Code);

    /// <summary>The Meta Conversions API. The base address ends in the Graph API version, such as <c>https://graph.facebook.com/v27.0/</c>.</summary>
    public sealed class MetaConversionsClient(HttpClient http)
    {
        public const string DefaultBaseUrl = "https://graph.facebook.com/v27.0/";

        /// <summary>
        /// Posts <paramref name="body"/> to the events edge of <paramref name="pixelId"/>, with the token as the
        /// <c>access_token</c> body parameter, so it never appears in the request address. Success is any 2xx answer.
        /// </summary>
        public async Task<PlatformResult> SendAsync(string pixelId, string token, JsonObject body, CancellationToken ct)
        {
            var payload = body.DeepClone().AsObject();
            payload["access_token"] = token;
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Uri.EscapeDataString(pixelId)}/events")
            {
                Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json")
            };
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return new PlatformResult(true, (int)response.StatusCode, null);
            // The error is {error:{message,type,code}}. Only the code is kept, since the message can echo the request.
            var code = await ErrorCodes.ReadAsync(response, ct, "error", "code");
            return new PlatformResult(false, (int)response.StatusCode, code);
        }
    }

    /// <summary>The TikTok Events API 2.0. The base address is <c>https://business-api.tiktok.com/open_api/v1.3/</c>.</summary>
    public sealed class TikTokEventsClient(HttpClient http)
    {
        public const string DefaultBaseUrl = "https://business-api.tiktok.com/open_api/v1.3/";

        /// <summary>Posts <paramref name="body"/> to <c>event/track/</c>. Success is HTTP 200 with <c>code</c> 0.</summary>
        public async Task<PlatformResult> SendAsync(string token, JsonObject body, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "event/track/")
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Access-Token", token);
            using var response = await http.SendAsync(request, ct);
            var code = await ErrorCodes.ReadAsync(response, ct, "code");
            return new PlatformResult(response.StatusCode == HttpStatusCode.OK && code == 0, (int)response.StatusCode, code);
        }
    }

    internal static class ErrorCodes
    {
        /// <summary>The number at <paramref name="path"/> in the JSON answer, or null when there is none.</summary>
        public static async Task<long?> ReadAsync(HttpResponseMessage response, CancellationToken ct, params string[] path)
        {
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var node = json.RootElement;
                foreach (var name in path)
                {
                    if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(name, out node)) return null;
                }
                return node.ValueKind == JsonValueKind.Number && node.TryGetInt64(out var code) ? code : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
