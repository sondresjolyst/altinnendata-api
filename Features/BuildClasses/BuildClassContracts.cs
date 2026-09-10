using FluentValidation;
using altinnendata_api.Constants;

namespace altinnendata_api.Features.BuildClasses
{
    public record BuildClassTranslationInput(string Locale, string Name, string? Description);

    public class BuildClassInput
    {
        public required string Key { get; set; }
        public int SortOrder { get; set; }
        public List<BuildClassTranslationInput> Translations { get; set; } = [];
    }

    /// <summary><paramref name="Name"/> and <paramref name="Description"/> are the requested locale's, falling back to the default one.</summary>
    public record BuildClassDto(
        int Id,
        string Key,
        string Name,
        string? Description,
        int SortOrder,
        IReadOnlyList<BuildClassTranslationInput> Translations);

    public class BuildClassValidator : AbstractValidator<BuildClassInput>
    {
        public BuildClassValidator()
        {
            RuleFor(x => x.Key)
                .NotEmpty().MaximumLength(60)
                .Matches("^[a-z0-9-]+$").WithMessage("Key may only contain lower-case letters, digits and hyphens.");

            RuleFor(x => x.Translations)
                .Must(list => list.Any(t => string.Equals(t.Locale, Locales.Default, StringComparison.OrdinalIgnoreCase)))
                .WithMessage($"A name for the default locale ({Locales.Default}) is required.");

            RuleFor(x => x.Translations)
                .Must(list => list.Select(t => t.Locale.ToLowerInvariant()).Distinct().Count() == list.Count)
                .WithMessage("Each locale may only appear once.");

            RuleForEach(x => x.Translations).ChildRules(t =>
            {
                t.RuleFor(x => x.Locale).NotEmpty().Must(Locales.IsSupported).WithMessage("Unsupported locale.");
                t.RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
                t.RuleFor(x => x.Description).MaximumLength(300);
            });
        }
    }
}
