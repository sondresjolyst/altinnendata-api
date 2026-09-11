using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;

namespace altinnendata_api.Features.ComponentConditions
{
    /// <summary>Read / create / update / delete the conditions an admin marks build parts with, with their per-locale name.</summary>
    public static class ComponentConditionEndpoints
    {
        public static async Task<IResult> GetAll(ApplicationDbContext db, CancellationToken ct, string? locale = null)
        {
            var resolved = Locales.Normalize(locale);
            var conditions = await db.ComponentConditions
                .AsNoTracking()
                .Include(c => c.Translations)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Key)
                .ToListAsync(ct);

            return TypedResults.Ok(conditions.Select(c => ToDto(c, resolved)));
        }

        public static async Task<IResult> Create(ComponentConditionInput body, ApplicationDbContext db, CancellationToken ct)
        {
            var key = body.Key.Trim().ToLowerInvariant();
            if (await db.ComponentConditions.AnyAsync(c => c.Key == key, ct))
                return TypedResults.Problem("A condition with that key already exists.", statusCode: StatusCodes.Status409Conflict);

            var condition = new ComponentCondition { Key = key, SortOrder = body.SortOrder };
            ApplyTranslations(condition, body);

            db.ComponentConditions.Add(condition);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(ToDto(condition, Locales.Default));
        }

        public static async Task<IResult> Update(int id, ComponentConditionInput body, ApplicationDbContext db, CancellationToken ct)
        {
            var condition = await db.ComponentConditions.Include(c => c.Translations).FirstOrDefaultAsync(c => c.Id == id, ct);
            if (condition == null) return TypedResults.NotFound();

            var key = body.Key.Trim().ToLowerInvariant();
            if (await db.ComponentConditions.AnyAsync(c => c.Id != id && c.Key == key, ct))
                return TypedResults.Problem("A condition with that key already exists.", statusCode: StatusCodes.Status409Conflict);

            condition.Key = key;
            condition.SortOrder = body.SortOrder;

            db.ComponentConditionTranslations.RemoveRange(condition.Translations);
            condition.Translations.Clear();
            ApplyTranslations(condition, body);

            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(ToDto(condition, Locales.Default));
        }

        /// <summary>Build parts keep their other fields; the condition is simply cleared from them.</summary>
        public static async Task<IResult> Delete(int id, ApplicationDbContext db, CancellationToken ct)
        {
            var condition = await db.ComponentConditions.FindAsync([id], ct);
            if (condition == null) return TypedResults.NotFound();

            db.ComponentConditions.Remove(condition);
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        }

        private static void ApplyTranslations(ComponentCondition condition, ComponentConditionInput body)
        {
            foreach (var translation in body.Translations)
            {
                condition.Translations.Add(new ComponentConditionTranslation
                {
                    Locale = Locales.Normalize(translation.Locale),
                    Name = translation.Name.Trim()
                });
            }
        }

        public static ComponentConditionDto ToDto(ComponentCondition condition, string locale) =>
            new(
                condition.Id,
                condition.Key,
                Pick(condition, locale)?.Name ?? condition.Key,
                condition.SortOrder,
                condition.Translations
                    .OrderBy(t => t.Locale)
                    .Select(t => new ComponentConditionTranslationInput(t.Locale, t.Name))
                    .ToList());

        public static ComponentConditionTranslation? Pick(ComponentCondition condition, string locale) =>
            condition.Translations.FirstOrDefault(t => t.Locale == locale)
            ?? condition.Translations.FirstOrDefault(t => t.Locale == Locales.Default);

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                app.MapGet("/api/component-conditions", GetAll).AllowAnonymous();

                var admin = app.MapGroup("/api/component-conditions").RequireAuthorization(Policies.Admin);
                admin.MapPost("", Create).WithValidation<ComponentConditionInput>();
                admin.MapPut("{id:int}", Update).WithValidation<ComponentConditionInput>();
                admin.MapDelete("{id:int}", Delete);
            }
        }
    }
}
