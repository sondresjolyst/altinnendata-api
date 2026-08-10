using System.Text.Json.Nodes;
using FluentValidation;
using altinnendata_api.Constants;

namespace altinnendata_api.Features.Builds
{
    public class BuildTranslationInput
    {
        public required string Locale { get; set; }
        public required string Title { get; set; }
        public string? Summary { get; set; }
        public JsonNode? Sections { get; set; }
    }

    public class BuildComponentInput
    {
        public int? ComponentPartId { get; set; }
        public int? ComponentCategoryId { get; set; }
        public string? Name { get; set; }
        public string? Details { get; set; }
        public int SortOrder { get; set; }
    }

    public class CreateBuildDto
    {
        public string? Category { get; set; }
        public string Availability { get; set; } = nameof(Models.BuildAvailability.Available);
        public int? PriceNok { get; set; }
        public DateOnly? BuiltOn { get; set; }
        public string? CoverImageId { get; set; }
        public bool Published { get; set; }
        public int SortOrder { get; set; }
        public List<BuildTranslationInput> Translations { get; set; } = [];
        public List<BuildComponentInput> Components { get; set; } = [];
    }

    public class UpdateBuildDto : CreateBuildDto { }

    public record BuildComponentDto(
        int Id,
        int? ComponentPartId,
        int? ComponentCategoryId,
        string? CategoryKey,
        string? CategoryName,
        string? ManufacturerName,
        string Name,
        string? Details,
        int SortOrder);

    /// <summary>List item: one locale's text plus the structured fields.</summary>
    public record BuildSummaryDto(
        int Id,
        string Slug,
        string? Category,
        string Availability,
        int? PriceNok,
        DateOnly? BuiltOn,
        string? CoverImageId,
        bool Published,
        int SortOrder,
        string Locale,
        string Title,
        string? Summary,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    public record BuildDetailDto(
        int Id,
        string Slug,
        string? Category,
        string Availability,
        int? PriceNok,
        DateOnly? BuiltOn,
        string? CoverImageId,
        bool Published,
        int SortOrder,
        string Locale,
        string Title,
        string? Summary,
        JsonNode Sections,
        IReadOnlyList<BuildComponentDto> Components,
        IReadOnlyList<string> AvailableLocales,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    /// <summary>Admin view: every translation, for the editor's language tabs.</summary>
    public record BuildAdminDto(
        int Id,
        string Slug,
        string? Category,
        string Availability,
        int? PriceNok,
        DateOnly? BuiltOn,
        string? CoverImageId,
        bool Published,
        int SortOrder,
        IReadOnlyList<BuildTranslationDto> Translations,
        IReadOnlyList<BuildComponentDto> Components,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    public record BuildTranslationDto(string Locale, string Title, string? Summary, JsonNode Sections);

    public abstract class BuildValidator<T> : AbstractValidator<T> where T : CreateBuildDto
    {
        protected BuildValidator()
        {
            RuleFor(x => x.Category).MaximumLength(60);
            RuleFor(x => x.CoverImageId).MaximumLength(32);
            RuleFor(x => x.PriceNok).GreaterThanOrEqualTo(0).When(x => x.PriceNok.HasValue);

            RuleFor(x => x.Availability)
                .Must(a => Enum.TryParse<Models.BuildAvailability>(a, ignoreCase: true, out _))
                .WithMessage("Availability must be Available, Reserved or Sold.");

            RuleFor(x => x.Translations)
                .NotEmpty().WithMessage("At least one translation is required.");

            RuleFor(x => x.Translations)
                .Must(list => list.Any(t => string.Equals(t.Locale, Locales.Default, StringComparison.OrdinalIgnoreCase)))
                .WithMessage($"A translation for the default locale ({Locales.Default}) is required.")
                .When(x => x.Translations.Count > 0);

            RuleFor(x => x.Translations)
                .Must(list => list.Select(t => t.Locale?.ToLowerInvariant()).Distinct().Count() == list.Count)
                .WithMessage("Each locale may only appear once.")
                .When(x => x.Translations.Count > 0);

            RuleForEach(x => x.Translations).ChildRules(t =>
            {
                t.RuleFor(x => x.Locale)
                    .NotEmpty()
                    .Must(Locales.IsSupported).WithMessage("Unsupported locale.");
                t.RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
                t.RuleFor(x => x.Summary).MaximumLength(400);
                t.RuleFor(x => x.Sections)
                    .Must(s => s is null or JsonArray).WithMessage("Sections must be a JSON array.");
            });

            RuleForEach(x => x.Components).ChildRules(c =>
            {
                c.RuleFor(x => x.Name).MaximumLength(200);
                c.RuleFor(x => x.Details).MaximumLength(300);
                c.RuleFor(x => x)
                    .Must(x => x.ComponentPartId.HasValue || !string.IsNullOrWhiteSpace(x.Name))
                    .WithMessage("A component needs either a catalog part or a name.");
            });
        }
    }

    public class CreateBuildValidator : BuildValidator<CreateBuildDto> { }

    public class UpdateBuildValidator : BuildValidator<UpdateBuildDto> { }
}
