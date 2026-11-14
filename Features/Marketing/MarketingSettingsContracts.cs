using System.Text.RegularExpressions;
using FluentValidation;

namespace altinnendata_api.Features.Marketing
{
    /// <summary>The admin's marketing settings. Each ID is null while that pixel is off.</summary>
    public record MarketingSettingsDto(string? MetaPixelId, string? TikTokPixelId);

    /// <summary>What the site needs to load its marketing tools. The app loads them only after the visitor's consent.</summary>
    public record MarketingConfigDto(string? MetaPixelId, string? TikTokPixelId);

    /// <summary>Replaces both pixel IDs. A missing ID turns that pixel off.</summary>
    public partial class UpdateMarketingSettingsDto
    {
        public const string MetaPixelIdField = "metaPixelId";
        public const string TikTokPixelIdField = "tikTokPixelId";

        /// <summary>The Meta Pixel ID, 5 to 20 digits. Empty or missing turns the Pixel off.</summary>
        public string? MetaPixelId { get; set; }

        /// <summary>The TikTok Pixel ID, 10 to 32 capital letters and digits. Empty or missing turns the Pixel off.</summary>
        public string? TikTokPixelId { get; set; }

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
        }
    }
}
