using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;

namespace altinnendata_api.Features.Components
{
    /// <summary>The whole catalog in one call: categories in display order, each with its parts.</summary>
    public static class GetComponentTree
    {
        public static async Task<IResult> Get(ApplicationDbContext db, CancellationToken ct, string? locale = null)
        {
            var resolved = Locales.Normalize(locale);

            var categories = await db.ComponentCategories
                .AsNoTracking()
                .Include(c => c.Translations)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Key)
                .ToListAsync(ct);

            var parts = await db.ComponentParts
                .AsNoTracking()
                .Include(p => p.Category)
                .Include(p => p.Manufacturer)
                .OrderBy(p => p.Manufacturer!.Name)
                .ThenBy(p => p.Name)
                .ToListAsync(ct);

            var byCategory = parts.ToLookup(p => p.CategoryId);

            var tree = categories.Select(c => new CategoryTreeDto(
                c.Id,
                c.Key,
                NameFor(c, resolved),
                c.SortOrder,
                byCategory[c.Id].Select(Parts.ToDto).ToList()));

            return TypedResults.Ok(tree);
        }

        private static string NameFor(ComponentCategory category, string locale) =>
            category.Translations.FirstOrDefault(t => t.Locale == locale)?.Name
            ?? category.Translations.FirstOrDefault(t => t.Locale == Locales.Default)?.Name
            ?? category.Key;

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app) =>
                app.MapGet("/api/components/tree", Get).AllowAnonymous();
        }
    }
}
