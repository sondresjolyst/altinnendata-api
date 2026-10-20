using FluentValidation;
using altinnendata_api.Constants;

namespace altinnendata_api.Features.Builds
{
    public class BuildTranslationInput
    {
        public required string Locale { get; set; }
        public required string Title { get; set; }
        public string? Summary { get; set; }
        public string? Description { get; set; }
    }

    public class BuildComponentInput
    {
        public int? ComponentPartId { get; set; }
        public int? ComponentCategoryId { get; set; }
        public int? ComponentConditionId { get; set; }
        public string? Name { get; set; }
        public string? Details { get; set; }
        public int SortOrder { get; set; }
    }

    public class CreateBuildDto
    {
        public string? Category { get; set; }
        public string Availability { get; set; } = nameof(Models.BuildAvailability.Available);
        public int? BuildClassId { get; set; }
        public int? PriceNok { get; set; }
        public DateOnly? BuiltOn { get; set; }

        /// <summary>Left null on a build turning Sold, the API stamps today.</summary>
        public DateOnly? SoldOn { get; set; }
        public string? FinnUrl { get; set; }
        public bool Published { get; set; }
        public int SortOrder { get; set; }
        public List<BuildTranslationInput> Translations { get; set; } = [];
        public List<BuildComponentInput> Components { get; set; } = [];
        public List<string> ImageIds { get; set; } = [];
    }

    public class UpdateBuildDto : CreateBuildDto
    {
        /// <summary>
        /// The <c>UpdatedAt</c> the edit form loaded. When given and the build has changed since, for example
        /// because a checkout now holds it, the update is refused. Left null by older clients, which skips the check.
        /// </summary>
        public DateTime? ExpectedUpdatedAt { get; set; }
    }

    public record BuildComponentDto(
        int Id,
        int? ComponentPartId,
        int? ComponentCategoryId,
        string? CategoryKey,
        string? CategoryName,
        string? ManufacturerName,
        string Name,
        string? Details,
        ComponentConditionRef? Condition,
        int SortOrder);

    /// <summary>List item: one locale's text plus the structured fields.</summary>
    public record BuildSummaryDto(
        int Id,
        string Slug,
        string? Category,
        string Availability,
        BuildClassRef? BuildClass,
        int? PriceNok,
        DateOnly? BuiltOn,
        DateOnly? SoldOn,
        string? CoverImageId,
        bool Published,
        int SortOrder,
        string Locale,
        string Title,
        string? Summary,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        IReadOnlyList<ComponentConditionRef> Conditions,
        bool AllPartsHaveCondition);

    public record BuildDetailDto(
        int Id,
        string Slug,
        string? Category,
        string Availability,
        BuildClassRef? BuildClass,
        int? PriceNok,
        DateOnly? BuiltOn,
        DateOnly? SoldOn,
        string? CoverImageId,
        string? FinnUrl,
        bool Published,
        int SortOrder,
        string Locale,
        string Title,
        string? Summary,
        string? Description,
        IReadOnlyList<string> ImageIds,
        IReadOnlyList<BuildComponentDto> Components,
        IReadOnlyList<string> AvailableLocales,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        IReadOnlyList<ComponentConditionRef> Conditions,
        bool AllPartsHaveCondition);

    /// <summary>Admin view: every translation, for the editor's language tabs.</summary>
    public record BuildAdminDto(
        int Id,
        string Slug,
        string? Category,
        string Availability,
        BuildClassRef? BuildClass,
        int? PriceNok,
        DateOnly? BuiltOn,
        DateOnly? SoldOn,
        string? CoverImageId,
        string? FinnUrl,
        bool Published,
        int SortOrder,
        IReadOnlyList<BuildTranslationDto> Translations,
        IReadOnlyList<BuildComponentDto> Components,
        IReadOnlyList<string> ImageIds,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        IReadOnlyList<ComponentConditionRef> Conditions,
        bool AllPartsHaveCondition);

    public record BuildTranslationDto(string Locale, string Title, string? Summary, string? Description);

    /// <summary>The build's class in one locale: what the card and the spec table show.</summary>
    public record BuildClassRef(int Id, string Key, string Name, string? Description);

    /// <summary>A part's condition in one locale, e.g. "Brukt".</summary>
    public record ComponentConditionRef(int Id, string Key, string Name);

    public abstract class BuildValidator<T> : AbstractValidator<T> where T : CreateBuildDto
    {
        protected BuildValidator()
        {
            RuleFor(x => x.Category).MaximumLength(60);
            RuleFor(x => x.PriceNok).GreaterThanOrEqualTo(0).When(x => x.PriceNok.HasValue);

            RuleFor(x => x.FinnUrl)
                .MaximumLength(400)
                .Must(BeAFinnLink)
                .When(x => !string.IsNullOrWhiteSpace(x.FinnUrl))
                .WithMessage("The advert link must point at finn.no.");

            // A day's slack: the admin's local date can be ahead of the UTC one.
            RuleFor(x => x.SoldOn)
                .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))
                .When(x => x.SoldOn.HasValue)
                .WithMessage("The sale date cannot be in the future.");

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
                t.RuleFor(x => x.Description).MaximumLength(8000);
            });

            RuleForEach(x => x.Components).ChildRules(c =>
            {
                c.RuleFor(x => x.Name).MaximumLength(200);
                c.RuleFor(x => x.Details).MaximumLength(300);
                c.RuleFor(x => x)
                    .Must(x => x.ComponentPartId.HasValue || !string.IsNullOrWhiteSpace(x.Name))
                    .WithMessage("A component needs either a catalog part or a name.");
            });

            // A build on sale must say what state every part is in (markedsføringsloven §§ 7 and 8).
            RuleFor(x => x.Components)
                .NotEmpty()
                .When(IsForSale)
                .WithMessage(ConditionRequiredMessage);
            RuleForEach(x => x.Components).ChildRules(c =>
            {
                c.RuleFor(x => x.ComponentConditionId).NotNull().WithMessage(ConditionRequiredMessage);
            }).When(IsForSale);

            RuleForEach(x => x.ImageIds).MaximumLength(32);
        }

        public const string ConditionRequiredMessage = "Choose a condition for every part before the build is put on sale.";

        /// <summary>Published, available and priced: what checkout needs to sell the build.</summary>
        private static bool IsForSale(T dto) =>
            dto.Published
            && dto.PriceNok.HasValue
            && string.Equals(dto.Availability, nameof(Models.BuildAvailability.Available), StringComparison.OrdinalIgnoreCase);

        private static bool BeAFinnLink(string? url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            && parsed.Scheme == Uri.UriSchemeHttps
            && (parsed.Host.Equals("finn.no", StringComparison.OrdinalIgnoreCase)
                || parsed.Host.EndsWith(".finn.no", StringComparison.OrdinalIgnoreCase));
    }

    public class CreateBuildValidator : BuildValidator<CreateBuildDto> { }

    public class UpdateBuildValidator : BuildValidator<UpdateBuildDto> { }
}
