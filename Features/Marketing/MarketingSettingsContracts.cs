using System.Text.RegularExpressions;
using FluentValidation;

namespace altinnendata_api.Features.Marketing
{
    /// <summary>The admin's marketing settings. <c>MetaPixelId</c> is null while the Meta Pixel is off.</summary>
    public record MarketingSettingsDto(string? MetaPixelId);

    /// <summary>What the site needs to load its marketing tools. The app loads them only after the visitor's consent.</summary>
    public record MarketingConfigDto(string? MetaPixelId);

    public partial class UpdateMarketingSettingsDto
    {
        public const string MetaPixelIdField = "metaPixelId";

        /// <summary>The Meta Pixel ID, 5 to 20 digits. Empty or missing turns the Pixel off.</summary>
        public string? MetaPixelId { get; set; }

        [GeneratedRegex("^[0-9]{5,20}$")]
        private static partial Regex PixelIdPattern();

        /// <summary>The trimmed ID, or null when <paramref name="value"/> is empty.</summary>
        public static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <summary>True when <paramref name="value"/> is empty or a Pixel ID of 5 to 20 ASCII digits.</summary>
        public static bool IsValidPixelId(string? value)
        {
            var id = Normalize(value);
            return id == null || PixelIdPattern().IsMatch(id);
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
        }
    }
}
