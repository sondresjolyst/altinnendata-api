using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Helpers;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Services;

namespace altinnendata_api.Features.ContentImages
{
    public record UploadedImage(string Id, string Url);

    public record ImageDimensions(string Id, int Width, int Height);

    /// <summary>Upload / serve / delete owner images used in site content.</summary>
    public static class ContentImages
    {
        // Per-image lock so the same image isn't backfilled twice concurrently.
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> BackfillLocks = new();

        // Responses are content-addressed (a new upload gets a new id), so they never change.
        private const string ImmutableCache = "public, max-age=31536000, immutable";
        // Short cache for the pre-backfill original fallback, so it can upgrade to webp later.
        private const string ShortCache = "public, max-age=3600";
        // A page references a handful of images; the cap is only a bound on a hostile request.
        private const int MaxDimensionIds = 100;

        public static async Task<IResult> Get(string id, int? w, HttpContext http, ApplicationDbContext db, IImageStorageService storage, CancellationToken ct)
        {
            var image = await db.ContentImages.Include(i => i.Variants).FirstOrDefaultAsync(i => i.Id == id, ct);
            if (image == null || !storage.Exists(image.StoredPath))
                return TypedResults.NotFound();

            http.Response.Headers.Vary = "Accept";
            http.Response.Headers.XContentTypeOptions = "nosniff";

            var acceptsWebp = http.Request.Headers.Accept.ToString().Contains("image/webp", StringComparison.OrdinalIgnoreCase);
            if (acceptsWebp)
            {
                var variants = image.Variants.Count > 0
                    ? image.Variants.ToList()
                    : await EnsureVariantsAsync(image.Id, image.StoredPath, db, storage, ct);

                var chosen = PickVariant(variants, w);
                if (chosen != null && storage.Exists(chosen.StoredPath))
                {
                    http.Response.Headers.CacheControl = ImmutableCache;
                    return TypedResults.File(storage.OpenRead(chosen.StoredPath), "image/webp");
                }

                http.Response.Headers.CacheControl = ShortCache;
                return TypedResults.File(storage.OpenRead(image.StoredPath), image.ContentType);
            }

            http.Response.Headers.CacheControl = ImmutableCache;
            return TypedResults.File(storage.OpenRead(image.StoredPath), image.ContentType);
        }

        public static async Task<IResult> Upload(IFormFile file, HttpContext http, ApplicationDbContext db, IImageStorageService storage, CancellationToken ct)
        {
            var (storedPath, contentType, sizeBytes) = await storage.SaveAsync(file, ct);
            var dimensions = storage.Probe(storedPath);

            var image = new ContentImage
            {
                FileName = Path.GetFileName(file.FileName),
                ContentType = contentType,
                SizeBytes = sizeBytes,
                StoredPath = storedPath,
                Width = dimensions?.Width ?? 0,
                Height = dimensions?.Height ?? 0,
                UploadedByUserId = http.User.UserId()
            };
            db.ContentImages.Add(image);
            await db.SaveChangesAsync(ct);

            await GenerateAndSaveVariantsAsync(image.Id, image.StoredPath, db, storage, ct);

            return TypedResults.Ok(new UploadedImage(image.Id, $"/content-images/{image.Id}"));
        }

        public static async Task<IResult> Delete(string id, ApplicationDbContext db, IImageStorageService storage, CancellationToken ct)
        {
            var image = await db.ContentImages.Include(i => i.Variants).FirstOrDefaultAsync(i => i.Id == id, ct);
            if (image == null) return TypedResults.NotFound();

            storage.Delete(image.StoredPath);
            foreach (var variant in image.Variants)
                storage.Delete(variant.StoredPath);

            db.ContentImages.Remove(image); // cascade removes variant rows
            await db.SaveChangesAsync(ct);
            return TypedResults.NoContent();
        }

        /// <summary>
        /// Intrinsic dimensions for a set of images, batched so a page needs one request for all
        /// of its own. An image with no dimensions recorded yet is measured here and the result
        /// kept; ids that are unknown, or whose file will not decode, are left out.
        /// </summary>
        public static async Task<IResult> Dimensions(string? ids, ApplicationDbContext db, IImageStorageService storage, CancellationToken ct)
        {
            var requested = (ids ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct()
                .Take(MaxDimensionIds)
                .ToList();

            if (requested.Count == 0) return TypedResults.Ok(Array.Empty<ImageDimensions>());

            var images = await db.ContentImages.Where(i => requested.Contains(i.Id)).ToListAsync(ct);

            var measured = images.Where(i => i.Width == 0 || i.Height == 0).ToList();
            foreach (var image in measured)
            {
                var measurement = storage.Exists(image.StoredPath) ? storage.Probe(image.StoredPath) : null;
                if (measurement == null) continue;
                image.Width = measurement.Value.Width;
                image.Height = measurement.Value.Height;
            }
            if (measured.Count > 0) await db.SaveChangesAsync(ct);

            return TypedResults.Ok(images
                .Where(i => i.Width > 0 && i.Height > 0)
                .Select(i => new ImageDimensions(i.Id, i.Width, i.Height))
                .ToArray());
        }

        private static ContentImageVariant? PickVariant(IReadOnlyCollection<ContentImageVariant> variants, int? w)
        {
            var ordered = variants.OrderBy(v => v.Width).ToList();
            if (ordered.Count == 0) return null;
            if (w == null) return ordered[^1];
            return ordered.FirstOrDefault(v => v.Width >= w) ?? ordered[^1];
        }

        private static async Task<List<ContentImageVariant>> EnsureVariantsAsync(string imageId, string storedPath, ApplicationDbContext db, IImageStorageService storage, CancellationToken ct)
        {
            var gate = BackfillLocks.GetOrAdd(imageId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                var existing = await db.ContentImageVariants.Where(v => v.ContentImageId == imageId).ToListAsync(ct);
                if (existing.Count > 0) return existing;
                return await GenerateAndSaveVariantsAsync(imageId, storedPath, db, storage, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Best-effort: fall back to the original rather than failing the request.
                return [];
            }
            finally
            {
                gate.Release();
            }
        }

        private static async Task<List<ContentImageVariant>> GenerateAndSaveVariantsAsync(string imageId, string storedPath, ApplicationDbContext db, IImageStorageService storage, CancellationToken ct)
        {
            var generated = await storage.GenerateWebpVariantsAsync(storedPath, ct);
            var rows = generated.Select(g => ContentImageVariant.From(imageId, g)).ToList();
            if (rows.Count > 0)
            {
                db.ContentImageVariants.AddRange(rows);
                await db.SaveChangesAsync(ct);
            }
            return rows;
        }

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                app.MapGet("/api/content-images/{id}", Get);
                app.MapGet("/api/content-images/dimensions", Dimensions).AllowAnonymous();
                app.MapPost("/api/content-images", Upload).RequireAuthorization(Policies.Admin).DisableAntiforgery();
                app.MapDelete("/api/content-images/{id}", Delete).RequireAuthorization(Policies.Admin);
            }
        }
    }
}
