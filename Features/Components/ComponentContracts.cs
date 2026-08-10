using FluentValidation;
using altinnendata_api.Constants;

namespace altinnendata_api.Features.Components
{
    public record CategoryTranslationInput(string Locale, string Name);

    public class CategoryInput
    {
        public required string Key { get; set; }
        public int SortOrder { get; set; }
        public List<CategoryTranslationInput> Translations { get; set; } = [];
    }

    public record ManufacturerInput(string Name);

    public class PartInput
    {
        public int CategoryId { get; set; }
        public int? ManufacturerId { get; set; }
        public required string Name { get; set; }
        public string? Details { get; set; }
    }

    public record CategoryDto(int Id, string Key, string Name, int SortOrder, IReadOnlyList<CategoryTranslationInput> Translations);

    public record ManufacturerDto(int Id, string Name);

    public record PartDto(int Id, int CategoryId, string CategoryKey, int? ManufacturerId, string? ManufacturerName, string Name, string? Details);

    public record CategoryTreeDto(int Id, string Key, string Name, int SortOrder, IReadOnlyList<PartDto> Parts);

    public class CategoryValidator : AbstractValidator<CategoryInput>
    {
        public CategoryValidator()
        {
            RuleFor(x => x.Key)
                .NotEmpty().MaximumLength(60)
                .Matches("^[a-z0-9-]+$").WithMessage("Key may only contain lower-case letters, digits and hyphens.");

            RuleFor(x => x.Translations)
                .Must(list => list.Any(t => string.Equals(t.Locale, Locales.Default, StringComparison.OrdinalIgnoreCase)))
                .WithMessage($"A name for the default locale ({Locales.Default}) is required.");

            RuleForEach(x => x.Translations).ChildRules(t =>
            {
                t.RuleFor(x => x.Locale).NotEmpty().Must(Locales.IsSupported).WithMessage("Unsupported locale.");
                t.RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
            });
        }
    }

    public class ManufacturerValidator : AbstractValidator<ManufacturerInput>
    {
        public ManufacturerValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(80);
    }

    public class PartValidator : AbstractValidator<PartInput>
    {
        public PartValidator()
        {
            RuleFor(x => x.CategoryId).GreaterThan(0);
            RuleFor(x => x.Name).NotEmpty().MaximumLength(160);
            RuleFor(x => x.Details).MaximumLength(300);
        }
    }
}
