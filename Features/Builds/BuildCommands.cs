using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Features.Shop;
using altinnendata_api.Helpers;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using SjolystInnovation.AppKit.Storage.Images;

namespace altinnendata_api.Features.Builds
{
    /// <summary>Admin create / update / delete for PC builds, including their per-locale text and parts list.</summary>
    public static class BuildCommands
    {
        public static async Task<IResult> Create(CreateBuildDto dto, ApplicationDbContext db, CancellationToken ct)
        {
            if (await UnknownClassAsync(dto.BuildClassId, db, ct))
                return TypedResults.Problem("That build class does not exist.", statusCode: StatusCodes.Status400BadRequest);
            if (await UnknownConditionAsync(dto, db, ct))
                return TypedResults.Problem("That component condition does not exist.", statusCode: StatusCodes.Status400BadRequest);

            var defaultTitle = DefaultTitle(dto);
            var build = new PcBuild
            {
                Slug = await UniqueSlugAsync(defaultTitle, null, db, ct),
                Category = dto.Category,
                Availability = ParseAvailability(dto.Availability),
                BuildClassId = dto.BuildClassId,
                PriceNok = dto.PriceNok,
                BuiltOn = dto.BuiltOn,
                SoldOn = SoldDate(ParseAvailability(dto.Availability), dto.SoldOn, null, wasSold: false),
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

        public static async Task<IResult> Update(int id, UpdateBuildDto dto, ApplicationDbContext db, IContentImageService images, CancellationToken ct)
        {
            var build = await db.PcBuilds
                .Include(b => b.Translations)
                .Include(b => b.BuildClass).ThenInclude(c => c!.Translations)
                .Include(b => b.Components)
                .Include(b => b.Images)
                .FirstOrDefaultAsync(b => b.Id == id, ct);
            if (build == null) return TypedResults.NotFound();
            // A form opened before a checkout held the build would otherwise clear the hold and allow a second sale.
            if (dto.ExpectedUpdatedAt is { } expected && !SameMillisecond(expected, build.UpdatedAt)) return BuildChanged();

            if (await UnknownClassAsync(dto.BuildClassId, db, ct))
                return TypedResults.Problem("That build class does not exist.", statusCode: StatusCodes.Status400BadRequest);
            if (await UnknownConditionAsync(dto, db, ct))
                return TypedResults.Problem("That component condition does not exist.", statusCode: StatusCodes.Status400BadRequest);

            var defaultTitle = DefaultTitle(dto);
            var currentTitle = build.Translations.FirstOrDefault(t => t.Locale == Locales.Default)?.Title;
            if (!string.Equals(currentTitle, defaultTitle, StringComparison.Ordinal))
                build.Slug = await UniqueSlugAsync(defaultTitle, build.Id, db, ct);

            build.Category = dto.Category;
            var availability = ParseAvailability(dto.Availability);
            build.SoldOn = SoldDate(availability, dto.SoldOn, build.SoldOn, build.Availability == BuildAvailability.Sold);
            // The admin's hand edit wins over the order flow, so the order no longer holds the build.
            if (availability != build.Availability) build.HeldByOrderId = null;
            build.Availability = availability;
            build.BuildClassId = dto.BuildClassId;
            build.PriceNok = dto.PriceNok;
            build.BuiltOn = dto.BuiltOn;
            build.FinnUrl = Trimmed(dto.FinnUrl);
            build.Published = dto.Published;
            build.SortOrder = dto.SortOrder;
            build.UpdatedAt = DateTime.UtcNow;

            var dropped = build.Images
                .Select(i => i.ContentImageId)
                .Where(imageId => !dto.ImageIds.Contains(imageId))
                .ToList();

            db.PcBuildTranslations.RemoveRange(build.Translations);
            build.Translations.Clear();
            ApplyTranslations(build, dto);

            db.PcBuildComponents.RemoveRange(build.Components);
            build.Components.Clear();
            ApplyComponents(build, dto);

            db.PcBuildImages.RemoveRange(build.Images);
            build.Images.Clear();
            ApplyImages(build, dto);

            if (!await TrySaveAsync(db, ct)) return BuildChanged();
            await images.DeleteOrphansAsync(dropped, ct);

            var updated = await LoadAsync(build.Id, db, ct);
            return TypedResults.Ok(BuildMapping.ToAdmin(updated!));
        }

        public static async Task<IResult> Delete(int id, ApplicationDbContext db, IContentImageService images, CancellationToken ct)
        {
            var build = await db.PcBuilds.Include(b => b.Images).FirstOrDefaultAsync(b => b.Id == id, ct);
            if (build == null) return TypedResults.NotFound();

            var imageIds = build.Images.Select(i => i.ContentImageId).ToList();

            db.PcBuilds.Remove(build);
            if (!await TrySaveAsync(db, ct)) return BuildChanged();
            await images.DeleteOrphansAsync(imageIds, ct);
            return TypedResults.NoContent();
        }

        /// <summary>False when the build changed since it was read, for example by a checkout holding it.</summary>
        private static async Task<bool> TrySaveAsync(ApplicationDbContext db, CancellationToken ct)
        {
            try
            {
                await db.SaveChangesAsync(ct);
                return true;
            }
            catch (DbUpdateConcurrencyException)
            {
                return false;
            }
        }

        private static IResult BuildChanged() =>
            ShopProblems.Code(StatusCodes.Status409Conflict, "buildChanged", "The build was changed by someone else. Reload the page and try again.");

        /// <summary>Compared to the millisecond, because a browser keeps a date no finer than that.</summary>
        private static bool SameMillisecond(DateTime a, DateTime b) => Milliseconds(a) == Milliseconds(b);

        private static long Milliseconds(DateTime value) =>
            (value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value).Ticks / TimeSpan.TicksPerMillisecond;

        private static async Task<PcBuild?> LoadAsync(int id, ApplicationDbContext db, CancellationToken ct) =>
            await db.PcBuilds
                .AsNoTracking()
                .Include(b => b.Translations)
                .Include(b => b.BuildClass).ThenInclude(c => c!.Translations)
                .Include(b => b.Components).ThenInclude(c => c.ComponentPart).ThenInclude(p => p!.Manufacturer)
                .Include(b => b.Components).ThenInclude(c => c.ComponentPart).ThenInclude(p => p!.Category).ThenInclude(c => c!.Translations)
                .Include(b => b.Components).ThenInclude(c => c.ComponentCategory).ThenInclude(c => c!.Translations)
                .Include(b => b.Components).ThenInclude(c => c.Condition).ThenInclude(c => c!.Translations)
                .Include(b => b.Images)
                .FirstOrDefaultAsync(b => b.Id == id, ct);

        private static string DefaultTitle(CreateBuildDto dto) =>
            dto.Translations.First(t => string.Equals(t.Locale, Locales.Default, StringComparison.OrdinalIgnoreCase)).Title;

        /// <summary>
        /// A date from the admin always wins. Today is stamped only on the edit that turns a build
        /// Sold, so a build that was already Sold - including one sold before this field existed -
        /// is never given an invented date by an unrelated edit. The date survives a build leaving
        /// Sold, so moving it back does not lose when it was sold; only Sold builds are counted.
        /// </summary>
        private static DateOnly? SoldDate(BuildAvailability availability, DateOnly? given, DateOnly? current, bool wasSold)
        {
            if (given != null) return given;
            if (availability == BuildAvailability.Sold && !wasSold) return DateOnly.FromDateTime(DateTime.UtcNow);
            return current;
        }

        private static async Task<bool> UnknownClassAsync(int? classId, ApplicationDbContext db, CancellationToken ct) =>
            classId != null && !await db.BuildClasses.AnyAsync(c => c.Id == classId, ct);

        private static async Task<bool> UnknownConditionAsync(CreateBuildDto dto, ApplicationDbContext db, CancellationToken ct)
        {
            var ids = dto.Components
                .Where(c => c.ComponentConditionId != null)
                .Select(c => c.ComponentConditionId!.Value)
                .Distinct()
                .ToList();
            if (ids.Count == 0) return false;
            return await db.ComponentConditions.CountAsync(c => ids.Contains(c.Id), ct) != ids.Count;
        }

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
                    ComponentConditionId = input.ComponentConditionId,
                    Name = string.IsNullOrWhiteSpace(input.Name) ? null : input.Name.Trim(),
                    Details = string.IsNullOrWhiteSpace(input.Details) ? null : input.Details.Trim(),
                    SortOrder = order++
                });
            }
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
