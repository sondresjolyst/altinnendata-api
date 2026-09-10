using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;

namespace altinnendata_api.Features.BuildClasses
{
    /// <summary>Read / create / update / delete the build classes an admin picks from, with their per-locale name and description.</summary>
    public static class BuildClassEndpoints
    {
        public static async Task<IResult> GetAll(ApplicationDbContext db, CancellationToken ct, string? locale = null)
        {
            var resolved = Locales.Normalize(locale);
            var classes = await db.BuildClasses
                .AsNoTracking()
                .Include(c => c.Translations)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Key)
                .ToListAsync(ct);

            return TypedResults.Ok(classes.Select(c => ToDto(c, resolved)));
        }

        public static async Task<IResult> Create(BuildClassInput body, ApplicationDbContext db, CancellationToken ct)
        {
            var key = body.Key.Trim().ToLowerInvariant();
            if (await db.BuildClasses.AnyAsync(c => c.Key == key, ct))
                return TypedResults.Problem("A class with that key already exists.", statusCode: StatusCodes.Status409Conflict);

            var buildClass = new BuildClass { Key = key, SortOrder = body.SortOrder };
            ApplyTranslations(buildClass, body);

            db.BuildClasses.Add(buildClass);
            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(ToDto(buildClass, Locales.Default));
        }

        public static async Task<IResult> Update(int id, BuildClassInput body, ApplicationDbContext db, CancellationToken ct)
        {
            var buildClass = await db.BuildClasses.Include(c => c.Translations).FirstOrDefaultAsync(c => c.Id == id, ct);
            if (buildClass == null) return TypedResults.NotFound();

            var key = body.Key.Trim().ToLowerInvariant();
            if (await db.BuildClasses.AnyAsync(c => c.Id != id && c.Key == key, ct))
                return TypedResults.Problem("A class with that key already exists.", statusCode: StatusCodes.Status409Conflict);

            buildClass.Key = key;
            buildClass.SortOrder = body.SortOrder;

            db.BuildClassTranslations.RemoveRange(buildClass.Translations);
            buildClass.Translations.Clear();
            ApplyTranslations(buildClass, body);

            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(ToDto(buildClass, Locales.Default));
        }

        /// <summary>Builds keep their other fields; the class is simply cleared from them.</summary>
        public static async Task<IResult> Delete(int id, ApplicationDbContext db, CancellationToken ct)
        {
            var buildClass = await db.BuildClasses.FindAsync([id], ct);
            if (buildClass == null) return TypedResults.NotFound();

            db.BuildClasses.Remove(buildClass);
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        }

        private static void ApplyTranslations(BuildClass buildClass, BuildClassInput body)
        {
            foreach (var translation in body.Translations)
            {
                buildClass.Translations.Add(new BuildClassTranslation
                {
                    Locale = Locales.Normalize(translation.Locale),
                    Name = translation.Name.Trim(),
                    Description = string.IsNullOrWhiteSpace(translation.Description) ? null : translation.Description.Trim()
                });
            }
        }

        public static BuildClassDto ToDto(BuildClass buildClass, string locale)
        {
            var translation = Pick(buildClass, locale);

            return new BuildClassDto(
                buildClass.Id,
                buildClass.Key,
                translation?.Name ?? buildClass.Key,
                translation?.Description,
                buildClass.SortOrder,
                buildClass.Translations
                    .OrderBy(t => t.Locale)
                    .Select(t => new BuildClassTranslationInput(t.Locale, t.Name, t.Description))
                    .ToList());
        }

        public static BuildClassTranslation? Pick(BuildClass buildClass, string locale) =>
            buildClass.Translations.FirstOrDefault(t => t.Locale == locale)
            ?? buildClass.Translations.FirstOrDefault(t => t.Locale == Locales.Default);

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                app.MapGet("/api/build-classes", GetAll).AllowAnonymous();

                var admin = app.MapGroup("/api/build-classes").RequireAuthorization(Policies.Admin);
                admin.MapPost("", Create).WithValidation<BuildClassInput>();
                admin.MapPut("{id:int}", Update).WithValidation<BuildClassInput>();
                admin.MapDelete("{id:int}", Delete);
            }
        }
    }
}
