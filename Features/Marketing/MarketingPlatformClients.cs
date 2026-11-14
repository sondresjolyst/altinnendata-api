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
        /// Posts <paramref name="body"/> to the events edge of <paramref name="pixelId"/> as the form body the docs show,
        /// <c>-F 'data=[...]' -F 'access_token=...'</c>: a <c>data</c> field with the events as a JSON array, the
        /// <c>access_token</c> field, and <c>test_event_code</c> when set. The token never appears in the request
        /// address. Success is any 2xx answer.
        /// </summary>
        public async Task<PlatformResult> SendAsync(string pixelId, string token, JsonObject body, CancellationToken ct)
        {
            var form = new MultipartFormDataContent();
            AddField(form, "data", body["data"]!.ToJsonString());
            AddField(form, "access_token", token);
            if (body["test_event_code"] is JsonNode testEventCode) AddField(form, "test_event_code", testEventCode.GetValue<string>());
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{Uri.EscapeDataString(pixelId)}/events") { Content = form };
            using var response = await http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return new PlatformResult(true, (int)response.StatusCode, null);
            // The error is {error:{message,type,code}}. Only the code is kept, since the message can echo the request.
            var code = await ErrorCodes.ReadAsync(response, ct, "error", "code");
            return new PlatformResult(false, (int)response.StatusCode, code);
        }

        /// <summary>A plain form field as curl <c>-F name=value</c> sends it: a quoted name and no content type of its own.</summary>
        private static void AddField(MultipartFormDataContent form, string name, string value)
        {
            var field = new StringContent(value, Encoding.UTF8);
            field.Headers.ContentType = null;
            field.Headers.ContentDisposition = new System.Net.Http.Headers.ContentDispositionHeaderValue("form-data") { Name = $"\"{name}\"" };
            form.Add(field);
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
