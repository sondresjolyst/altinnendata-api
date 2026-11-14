using System.Text.RegularExpressions;
using FluentValidation;

namespace altinnendata_api.Features.Marketing
{
    /// <summary>
    /// The admin's marketing settings. Each ID is null while that pixel is off. The access tokens are write-only,
    /// so only whether each one is set is returned.
    /// </summary>
    public record MarketingSettingsDto(string? MetaPixelId, string? TikTokPixelId, bool MetaCapiTokenSet, bool TikTokEventsTokenSet);

    /// <summary>What the site needs to load its marketing tools. The app loads them only after the visitor's consent.</summary>
    public record MarketingConfigDto(string? MetaPixelId, string? TikTokPixelId);

    /// <summary>
    /// Replaces both pixel IDs. A missing ID turns that pixel off. Each access token is kept when missing or null,
    /// cleared when empty, and replaced otherwise.
    /// </summary>
    public partial class UpdateMarketingSettingsDto
    {
        public const string MetaPixelIdField = "metaPixelId";
        public const string TikTokPixelIdField = "tikTokPixelId";
        public const string MetaCapiTokenField = "metaCapiToken";
        public const string TikTokEventsTokenField = "tikTokEventsToken";
        public const int MinTokenLength = 20;
        public const int MaxTokenLength = 512;

        /// <summary>The Meta Pixel ID, 5 to 20 digits. Empty or missing turns the Pixel off.</summary>
        public string? MetaPixelId { get; set; }

        /// <summary>The TikTok Pixel ID, 10 to 32 capital letters and digits. Empty or missing turns the Pixel off.</summary>
        public string? TikTokPixelId { get; set; }

        /// <summary>The Meta Conversions API access token. Null keeps the stored token, empty clears it.</summary>
        public string? MetaCapiToken { get; set; }

        /// <summary>The TikTok Events API access token. Null keeps the stored token, empty clears it.</summary>
        public string? TikTokEventsToken { get; set; }

        [GeneratedRegex("^[0-9]{5,20}$")]
        private static partial Regex PixelIdPattern();

        // TikTok documents no format. Its one example ID is 20 capital letters and digits, so the rule is loose on purpose.
        [GeneratedRegex("^[A-Z0-9]{10,32}$")]
        private static partial Regex TikTokPixelIdPattern();

        /// <summary>The trimmed ID, or null when <paramref name="value"/> is empty.</summary>
        public static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <summary>True when <paramref name="value"/> is empty or a Pixel ID of 5 to 20 ASCII digits.</summary>
        public static bool IsValidPixelId(string? value)
        {
            var id = Normalize(value);
            return id == null || PixelIdPattern().IsMatch(id);
        }

        /// <summary>True when <paramref name="value"/> is empty or a TikTok Pixel ID of 10 to 32 ASCII capital letters and digits.</summary>
        public static bool IsValidTikTokPixelId(string? value)
        {
            var id = Normalize(value);
            return id == null || TikTokPixelIdPattern().IsMatch(id);
        }

        /// <summary>True when <paramref name="value"/> is null, empty, or a trimmed token of 20 to 512 printable ASCII characters without spaces.</summary>
        public static bool IsValidToken(string? value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            var token = value.Trim();
            return token.Length is >= MinTokenLength and <= MaxTokenLength && token.All(c => c is > ' ' and <= '~');
        }

        /// <summary>The token to store: <paramref name="stored"/> when <paramref name="value"/> is null, null when it is empty, else the trimmed value.</summary>
        public static string? NextToken(string? value, string? stored) =>
            value == null ? stored : value.Length == 0 ? null : value.Trim();
    }

    public class UpdateMarketingSettingsValidator : AbstractValidator<UpdateMarketingSettingsDto>
    {
        public UpdateMarketingSettingsValidator()
        {
            RuleFor(x => x.MetaPixelId)
                .Must(UpdateMarketingSettingsDto.IsValidPixelId)
                .OverridePropertyName(UpdateMarketingSettingsDto.MetaPixelIdField)
                .WithMessage("The Meta Pixel ID must be 5 to 20 digits.");

            RuleFor(x => x.TikTokPixelId)
                .Must(UpdateMarketingSettingsDto.IsValidTikTokPixelId)
                .OverridePropertyName(UpdateMarketingSettingsDto.TikTokPixelIdField)
                .WithMessage("The TikTok Pixel ID must be 10 to 32 capital letters and digits.");

            RuleFor(x => x.MetaCapiToken)
                .Must(UpdateMarketingSettingsDto.IsValidToken)
                .OverridePropertyName(UpdateMarketingSettingsDto.MetaCapiTokenField)
                .WithMessage("The Meta Conversions API token must be 20 to 512 printable characters without spaces.");

            RuleFor(x => x.TikTokEventsToken)
                .Must(UpdateMarketingSettingsDto.IsValidToken)
                .OverridePropertyName(UpdateMarketingSettingsDto.TikTokEventsTokenField)
                .WithMessage("The TikTok Events API token must be 20 to 512 printable characters without spaces.");
        }
    }
}
