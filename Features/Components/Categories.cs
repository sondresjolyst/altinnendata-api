using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;

namespace altinnendata_api.Features.Components
{
    /// <summary>Create / update / delete a component category (CPU, GPU, kabinett …) and its per-locale names.</summary>
    public static class Categories
    {
        public static async Task<IResult> GetAll(ApplicationDbContext db, CancellationToken ct, string? locale = null)
        {
            var resolved = Locales.Normalize(locale);
            var categories = await db.ComponentCategories
                .AsNoTracking()
                .Include(c => c.Translations)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Key)
                .ToListAsync(ct);

            return TypedResults.Ok(categories.Select(c => ToDto(c, resolved)));
        }

        public static async Task<IResult> Create(CategoryInput body, ApplicationDbContext db, CancellationToken ct)
        {
            var key = body.Key.Trim().ToLowerInvariant();
            if (await db.ComponentCategories.AnyAsync(c => c.Key == key, ct))
                return TypedResults.Problem("A category with that key already exists.", statusCode: StatusCodes.Status409Conflict);

            var category = new ComponentCategory { Key = key, SortOrder = body.SortOrder };
            ApplyTranslations(category, body);

            db.ComponentCategories.Add(category);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(ToDto(category, Locales.Default));
        }

        public static async Task<IResult> Update(int id, CategoryInput body, ApplicationDbContext db, CancellationToken ct)
        {
            var category = await db.ComponentCategories.Include(c => c.Translations).FirstOrDefaultAsync(c => c.Id == id, ct);
            if (category == null) return TypedResults.NotFound();

            var key = body.Key.Trim().ToLowerInvariant();
            if (await db.ComponentCategories.AnyAsync(c => c.Id != id && c.Key == key, ct))
                return TypedResults.Problem("A category with that key already exists.", statusCode: StatusCodes.Status409Conflict);

            category.Key = key;
            category.SortOrder = body.SortOrder;

            db.ComponentCategoryTranslations.RemoveRange(category.Translations);
            category.Translations.Clear();
            ApplyTranslations(category, body);

            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(ToDto(category, Locales.Default));
        }

        public static async Task<IResult> Delete(int id, ApplicationDbContext db, CancellationToken ct)
        {
            var category = await db.ComponentCategories.FindAsync([id], ct);
            if (category == null) return TypedResults.NotFound();

            if (await db.ComponentParts.AnyAsync(p => p.CategoryId == id, ct))
                return TypedResults.Problem("Remove the parts in this category first.", statusCode: StatusCodes.Status409Conflict);

            db.ComponentCategories.Remove(category);
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        }

        private static void ApplyTranslations(ComponentCategory category, CategoryInput body)
        {
            foreach (var translation in body.Translations)
            {
                category.Translations.Add(new ComponentCategoryTranslation
                {
                    Locale = Locales.Normalize(translation.Locale),
                    Name = translation.Name.Trim()
                });
            }
        }

        private static CategoryDto ToDto(ComponentCategory category, string locale)
        {
            var name = category.Translations.FirstOrDefault(t => t.Locale == locale)?.Name
                ?? category.Translations.FirstOrDefault(t => t.Locale == Locales.Default)?.Name
                ?? category.Key;

            return new CategoryDto(
                category.Id,
                category.Key,
                name,
                category.SortOrder,
                category.Translations.OrderBy(t => t.Locale).Select(t => new CategoryTranslationInput(t.Locale, t.Name)).ToList());
        }

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                app.MapGet("/api/components/categories", GetAll).AllowAnonymous();

                var admin = app.MapGroup("/api/components/categories").RequireAuthorization(Policies.Admin);
                admin.MapPost("", Create).WithValidation<CategoryInput>();
                admin.MapPut("{id:int}", Update).WithValidation<CategoryInput>();
                admin.MapDelete("{id:int}", Delete);
            }
        }
    }
}
