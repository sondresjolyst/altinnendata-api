using System.Text.Json.Nodes;
using altinnendata_api.Constants;
using altinnendata_api.Models;

namespace altinnendata_api.Features.Builds
{
    /// <summary>Turns a build plus its translations into the locale-resolved shapes the API returns.</summary>
    public static class BuildMapping
    {
        public static JsonNode ParseSections(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new JsonArray();
            try
            {
                return JsonNode.Parse(json) as JsonArray ?? new JsonArray();
            }
            catch (System.Text.Json.JsonException)
            {
                return new JsonArray();
            }
        }

        /// <summary>Requested locale, else the default locale, else whatever exists.</summary>
        public static PcBuildTranslation? PickTranslation(PcBuild build, string locale) =>
            build.Translations.FirstOrDefault(t => t.Locale == locale)
            ?? build.Translations.FirstOrDefault(t => t.Locale == Locales.Default)
            ?? build.Translations.FirstOrDefault();

        public static string CategoryName(ComponentCategory? category, string locale)
        {
            if (category == null) return string.Empty;
            var translation = category.Translations.FirstOrDefault(t => t.Locale == locale)
                ?? category.Translations.FirstOrDefault(t => t.Locale == Locales.Default)
                ?? category.Translations.FirstOrDefault();
            return translation?.Name ?? category.Key;
        }

        public static BuildComponentDto ToComponentDto(PcBuildComponent component, string locale)
        {
            var category = component.ComponentPart?.Category ?? component.ComponentCategory;
            var name = component.Name
                ?? (component.ComponentPart == null
                    ? string.Empty
                    : string.Join(' ', new[] { component.ComponentPart.Manufacturer?.Name, component.ComponentPart.Name }
                        .Where(s => !string.IsNullOrWhiteSpace(s))));

            return new BuildComponentDto(
                component.Id,
                component.ComponentPartId,
                category?.Id,
                category?.Key,
                category == null ? null : CategoryName(category, locale),
                component.ComponentPart?.Manufacturer?.Name,
                name,
                component.Details ?? component.ComponentPart?.Details,
                component.SortOrder);
        }

        public static BuildSummaryDto ToSummary(PcBuild build, string locale)
        {
            var translation = PickTranslation(build, locale);
            return new BuildSummaryDto(
                build.Id,
                build.Slug,
                build.Category,
                build.Availability.ToString(),
                build.PriceNok,
                build.BuiltOn,
                build.CoverImageId,
                build.Published,
                build.SortOrder,
                translation?.Locale ?? locale,
                translation?.Title ?? build.Slug,
                translation?.Summary,
                build.CreatedAt,
                build.UpdatedAt);
        }

        public static BuildDetailDto ToDetail(PcBuild build, string locale)
        {
            var translation = PickTranslation(build, locale);
            return new BuildDetailDto(
                build.Id,
                build.Slug,
                build.Category,
                build.Availability.ToString(),
                build.PriceNok,
                build.BuiltOn,
                build.CoverImageId,
                build.Published,
                build.SortOrder,
                translation?.Locale ?? locale,
                translation?.Title ?? build.Slug,
                translation?.Summary,
                ParseSections(translation?.SectionsJson),
                build.Components.OrderBy(c => c.SortOrder).Select(c => ToComponentDto(c, locale)).ToList(),
                build.Translations.Select(t => t.Locale).OrderBy(l => l).ToList(),
                build.CreatedAt,
                build.UpdatedAt);
        }

        public static BuildAdminDto ToAdmin(PcBuild build) => new(
            build.Id,
            build.Slug,
            build.Category,
            build.Availability.ToString(),
            build.PriceNok,
            build.BuiltOn,
            build.CoverImageId,
            build.Published,
            build.SortOrder,
            build.Translations
                .OrderBy(t => t.Locale == Locales.Default ? 0 : 1)
                .ThenBy(t => t.Locale)
                .Select(t => new BuildTranslationDto(t.Locale, t.Title, t.Summary, ParseSections(t.SectionsJson)))
                .ToList(),
            build.Components.OrderBy(c => c.SortOrder).Select(c => ToComponentDto(c, Locales.Default)).ToList(),
            build.CreatedAt,
            build.UpdatedAt);
    }
}
