using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Helpers;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Services;

namespace altinnendata_api.Features.Builds
{
    /// <summary>Admin create / update / delete for PC builds, including their per-locale text and parts list.</summary>
    public static class BuildCommands
    {
        public static async Task<IResult> Create(CreateBuildDto dto, ApplicationDbContext db, CancellationToken ct)
        {
            var defaultTitle = DefaultTitle(dto);
            var build = new PcBuild
            {
                Slug = await UniqueSlugAsync(defaultTitle, null, db, ct),
                Category = dto.Category,
                Availability = ParseAvailability(dto.Availability),
                PriceNok = dto.PriceNok,
                BuiltOn = dto.BuiltOn,
                CoverImageId = dto.CoverImageId,
                Published = dto.Published,
                SortOrder = dto.SortOrder,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            ApplyTranslations(build, dto);
            ApplyComponents(build, dto);

            db.PcBuilds.Add(build);
            await db.SaveChangesAsync(ct);

            var created = await LoadAsync(build.Id, db, ct);
            return TypedResults.Created($"/api/builds/{build.Slug}", BuildMapping.ToAdmin(created!));
        }

        public static async Task<IResult> Update(int id, UpdateBuildDto dto, ApplicationDbContext db, IImageStorageService images, CancellationToken ct)
        {
            var build = await db.PcBuilds
                .Include(b => b.Translations)
                .Include(b => b.Components)
                .FirstOrDefaultAsync(b => b.Id == id, ct);
            if (build == null) return TypedResults.NotFound();

            var defaultTitle = DefaultTitle(dto);
            var currentTitle = build.Translations.FirstOrDefault(t => t.Locale == Locales.Default)?.Title;
            if (!string.Equals(currentTitle, defaultTitle, StringComparison.Ordinal))
                build.Slug = await UniqueSlugAsync(defaultTitle, build.Id, db, ct);

            build.Category = dto.Category;
            build.Availability = ParseAvailability(dto.Availability);
            build.PriceNok = dto.PriceNok;
            build.BuiltOn = dto.BuiltOn;
            build.Published = dto.Published;
            build.SortOrder = dto.SortOrder;
            build.UpdatedAt = DateTime.UtcNow;

            await ApplyCoverImageAsync(build, dto.CoverImageId, db, images, ct);

            db.PcBuildTranslations.RemoveRange(build.Translations);
            build.Translations.Clear();
            ApplyTranslations(build, dto);

            db.PcBuildComponents.RemoveRange(build.Components);
            build.Components.Clear();
            ApplyComponents(build, dto);

            await db.SaveChangesAsync(ct);

            var updated = await LoadAsync(build.Id, db, ct);
            return TypedResults.Ok(BuildMapping.ToAdmin(updated!));
        }

        public static async Task<IResult> Delete(int id, ApplicationDbContext db, IImageStorageService images, CancellationToken ct)
        {
            var build = await db.PcBuilds.Include(b => b.CoverImage).ThenInclude(i => i!.Variants).FirstOrDefaultAsync(b => b.Id == id, ct);
            if (build == null) return TypedResults.NotFound();

            if (build.CoverImage != null)
                DeleteImage(build.CoverImage, db, images);

            db.PcBuilds.Remove(build);
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        }

        private static async Task<PcBuild?> LoadAsync(int id, ApplicationDbContext db, CancellationToken ct) =>
            await db.PcBuilds
                .AsNoTracking()
                .Include(b => b.Translations)
                .Include(b => b.Components).ThenInclude(c => c.ComponentPart).ThenInclude(p => p!.Manufacturer)
                .Include(b => b.Components).ThenInclude(c => c.ComponentPart).ThenInclude(p => p!.Category).ThenInclude(c => c!.Translations)
                .Include(b => b.Components).ThenInclude(c => c.ComponentCategory).ThenInclude(c => c!.Translations)
                .FirstOrDefaultAsync(b => b.Id == id, ct);

        private static string DefaultTitle(CreateBuildDto dto) =>
            dto.Translations.First(t => string.Equals(t.Locale, Locales.Default, StringComparison.OrdinalIgnoreCase)).Title;

        private static BuildAvailability ParseAvailability(string value) =>
            Enum.Parse<BuildAvailability>(value, ignoreCase: true);

        private static void ApplyTranslations(PcBuild build, CreateBuildDto dto)
        {
            foreach (var input in dto.Translations)
            {
                build.Translations.Add(new PcBuildTranslation
                {
                    Locale = Locales.Normalize(input.Locale),
                    Title = input.Title.Trim(),
                    Summary = string.IsNullOrWhiteSpace(input.Summary) ? null : input.Summary.Trim(),
                    SectionsJson = input.Sections?.ToJsonString() ?? "[]"
                });
            }
        }

        private static void ApplyComponents(PcBuild build, CreateBuildDto dto)
        {
            var order = 0;
            foreach (var input in dto.Components.OrderBy(c => c.SortOrder))
            {
                build.Components.Add(new PcBuildComponent
                {
                    ComponentPartId = input.ComponentPartId,
                    ComponentCategoryId = input.ComponentCategoryId,
                    Name = string.IsNullOrWhiteSpace(input.Name) ? null : input.Name.Trim(),
                    Details = string.IsNullOrWhiteSpace(input.Details) ? null : input.Details.Trim(),
                    SortOrder = order++
                });
            }
        }

        /// <summary>Swaps the cover image, deleting the replaced one so it does not linger on disk.</summary>
        private static async Task ApplyCoverImageAsync(PcBuild build, string? newImageId, ApplicationDbContext db, IImageStorageService images, CancellationToken ct)
        {
            if (build.CoverImageId == newImageId) return;

            var oldImageId = build.CoverImageId;
            build.CoverImageId = newImageId;

            if (oldImageId == null) return;

            var old = await db.ContentImages.Include(i => i.Variants).FirstOrDefaultAsync(i => i.Id == oldImageId, ct);
            if (old != null)
                DeleteImage(old, db, images);
        }

        private static void DeleteImage(ContentImage image, ApplicationDbContext db, IImageStorageService images)
        {
            images.Delete(image.StoredPath);
            foreach (var variant in image.Variants)
                images.Delete(variant.StoredPath);
            db.ContentImages.Remove(image);
        }

        private static async Task<string> UniqueSlugAsync(string title, int? excludeId, ApplicationDbContext db, CancellationToken ct)
        {
            var baseSlug = Slugify.Create(title);
            if (string.IsNullOrEmpty(baseSlug)) baseSlug = "bygg";

            var slug = baseSlug;
            var suffix = 2;
            while (await db.PcBuilds.AnyAsync(b => b.Slug == slug && b.Id != excludeId, ct))
            {
                slug = $"{baseSlug}-{suffix}";
                suffix++;
            }
            return slug;
        }

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                var group = app.MapGroup("/api/builds").RequireAuthorization(Policies.Admin);
                group.MapPost("", Create).WithValidation<CreateBuildDto>();
                group.MapPut("{id:int}", Update).WithValidation<UpdateBuildDto>();
                group.MapDelete("{id:int}", Delete);
            }
        }
    }
}
