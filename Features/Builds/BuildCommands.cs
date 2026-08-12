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
                FinnUrl = Trimmed(dto.FinnUrl),
                Published = dto.Published,
                SortOrder = dto.SortOrder,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            ApplyTranslations(build, dto);
            ApplyComponents(build, dto);
            ApplyImages(build, dto);

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
                .Include(b => b.Images)
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
            build.FinnUrl = Trimmed(dto.FinnUrl);
            build.Published = dto.Published;
            build.SortOrder = dto.SortOrder;
            build.UpdatedAt = DateTime.UtcNow;

            await RemoveDroppedImagesAsync(build, dto.ImageIds, db, images, ct);

            db.PcBuildTranslations.RemoveRange(build.Translations);
            build.Translations.Clear();
            ApplyTranslations(build, dto);

            db.PcBuildComponents.RemoveRange(build.Components);
            build.Components.Clear();
            ApplyComponents(build, dto);

            db.PcBuildImages.RemoveRange(build.Images);
            build.Images.Clear();
            ApplyImages(build, dto);

            await db.SaveChangesAsync(ct);

            var updated = await LoadAsync(build.Id, db, ct);
            return TypedResults.Ok(BuildMapping.ToAdmin(updated!));
        }

        public static async Task<IResult> Delete(int id, ApplicationDbContext db, IImageStorageService images, CancellationToken ct)
        {
            var build = await db.PcBuilds.Include(b => b.Images).FirstOrDefaultAsync(b => b.Id == id, ct);
            if (build == null) return TypedResults.NotFound();

            await DeleteUnusedImagesAsync(build.Images.Select(i => i.ContentImageId).ToList(), build.Id, db, images, ct);

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
                .Include(b => b.Images)
                .FirstOrDefaultAsync(b => b.Id == id, ct);

        private static string DefaultTitle(CreateBuildDto dto) =>
            dto.Translations.First(t => string.Equals(t.Locale, Locales.Default, StringComparison.OrdinalIgnoreCase)).Title;

        private static BuildAvailability ParseAvailability(string value) =>
            Enum.Parse<BuildAvailability>(value, ignoreCase: true);

        private static string? Trimmed(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static void ApplyTranslations(PcBuild build, CreateBuildDto dto)
        {
            foreach (var input in dto.Translations)
            {
                build.Translations.Add(new PcBuildTranslation
                {
                    Locale = Locales.Normalize(input.Locale),
                    Title = input.Title.Trim(),
                    Summary = Trimmed(input.Summary),
                    Description = Trimmed(input.Description)
                });
            }
        }

        private static void ApplyImages(PcBuild build, CreateBuildDto dto)
        {
            var order = 0;
            foreach (var imageId in dto.ImageIds.Distinct())
            {
                build.Images.Add(new PcBuildImage { ContentImageId = imageId, SortOrder = order++ });
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

        private static async Task RemoveDroppedImagesAsync(PcBuild build, List<string> keptIds, ApplicationDbContext db, IImageStorageService images, CancellationToken ct)
        {
            var dropped = build.Images
                .Select(i => i.ContentImageId)
                .Where(id => !keptIds.Contains(id))
                .ToList();

            await DeleteUnusedImagesAsync(dropped, build.Id, db, images, ct);
        }

        private static async Task DeleteUnusedImagesAsync(List<string> imageIds, int buildId, ApplicationDbContext db, IImageStorageService images, CancellationToken ct)
        {
            if (imageIds.Count == 0) return;

            var usedElsewhere = await db.PcBuildImages
                .Where(i => imageIds.Contains(i.ContentImageId) && i.PcBuildId != buildId)
                .Select(i => i.ContentImageId)
                .ToListAsync(ct);

            var orphans = await db.ContentImages
                .Include(i => i.Variants)
                .Where(i => imageIds.Contains(i.Id) && !usedElsewhere.Contains(i.Id))
                .ToListAsync(ct);

            foreach (var image in orphans)
                DeleteImage(image, db, images);
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
