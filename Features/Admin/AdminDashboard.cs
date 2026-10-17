using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Admin;
using altinnendata_api.Services;
using Microsoft.Extensions.Options;
using SjolystInnovation.AppKit.AdminStats;
using SjolystInnovation.AppKit.Storage.Images;

namespace altinnendata_api.Features.Admin
{
    /// <summary>Admin dashboard: aggregate stats, daily history, roles, Brevo email stats.</summary>
    public static class AdminDashboard
    {
        public static async Task<IResult> GetStats(ApplicationDbContext db, IOptions<AppKitContentImagesOptions> images, ILoggerFactory loggerFactory, CancellationToken ct)
        {
            var imagesPath = Path.GetFullPath(images.Value.ImagesPath);

            long diskTotal = 0, diskFree = 0;
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(imagesPath) ?? imagesPath);
                diskTotal = drive.TotalSize;
                diskFree = drive.AvailableFreeSpace;
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("Admin").LogWarning("Disk info unavailable: {Error}", ex.Message);
            }

            var sold = await db.PcBuilds
                .Where(b => b.Availability == BuildAvailability.Sold)
                .Select(b => new { b.PriceNok, b.SoldOn })
                .ToListAsync(ct);

            return TypedResults.Ok(new AdminStatsDto
            {
                TotalUsers = await db.Users.CountAsync(u => !u.IsDeleted, ct),
                PublishedBuilds = await db.PcBuilds.CountAsync(b => b.Published, ct),
                DraftBuilds = await db.PcBuilds.CountAsync(b => !b.Published, ct),
                SoldBuilds = sold.Count,
                ReservedBuilds = await db.PcBuilds.CountAsync(b => b.Availability == BuildAvailability.Reserved, ct),
                AvailableBuilds = await db.PcBuilds.CountAsync(b => b.Availability == BuildAvailability.Available, ct),
                RevenueNok = sold.Where(b => b.PriceNok.HasValue).Sum(b => (long)b.PriceNok!.Value),
                SoldWithoutPrice = sold.Count(b => !b.PriceNok.HasValue),
                SoldWithoutDate = sold.Count(b => !b.SoldOn.HasValue),
                CatalogParts = await db.ComponentParts.CountAsync(ct),
                ContentImages = await db.ContentImages.CountAsync(ct),
                StorageUsedBytes = DirectorySize(imagesPath),
                DiskTotalBytes = diskTotal,
                DiskFreeBytes = diskFree
            });
        }

        /// <summary>
        /// The package's daily history with SoldBuilds added. The package serves only stored counters, and
        /// sold counts are derived instead, so this endpoint stays in the app.
        /// </summary>
        public static async Task<IResult> GetStatsHistory(ApplicationDbContext db, AppKitDailyStats<ApplicationDbContext, DailyStatSnapshot> stats, CancellationToken ct, DateOnly? from = null)
        {
            var snapshots = await stats.GetHistoryAsync(db, from, ct);

            // Sold counts are not snapshotted but derived from each sale's own date, so a sale
            // dated before the snapshot table existed still lands on the right day. The flip side:
            // unlike the frozen series, past days move when a build is deleted or un-sold.
            var soldDates = await db.PcBuilds
                .Where(b => b.Availability == BuildAvailability.Sold && b.SoldOn != null)
                .Select(b => b.SoldOn!.Value)
                .ToListAsync(ct);

            var result = snapshots.Select(s => new DailyStatDto
            {
                Date = s.Date.ToString("yyyy-MM-dd"),
                TotalUsers = s.TotalUsers,
                PublishedBuilds = s.PublishedBuilds,
                DraftBuilds = s.DraftBuilds,
                SoldBuilds = soldDates.Count(d => d <= s.Date),
                CatalogParts = s.CatalogParts,
                ContentImages = s.ContentImages
            }).ToList();
            return TypedResults.Ok(result);
        }

        /// <summary>The counts the daily history stores, as they are right now.</summary>
        public static async Task<DailyStatSnapshot> SampleAsync(ApplicationDbContext db, CancellationToken ct) => new()
        {
            TotalUsers = await db.Users.CountAsync(u => !u.IsDeleted, ct),
            PublishedBuilds = await db.PcBuilds.CountAsync(b => b.Published, ct),
            DraftBuilds = await db.PcBuilds.CountAsync(b => !b.Published, ct),
            CatalogParts = await db.ComponentParts.CountAsync(ct),
            ContentImages = await db.ContentImages.CountAsync(ct)
        };

        public static IResult GetRoles() => TypedResults.Ok(RoleNames.AllRoles);

        public static async Task<IResult> GetEmailStats(IEmailService email, ILoggerFactory loggerFactory, int days = 30)
        {
            try
            {
                return TypedResults.Ok(await email.GetEmailStatsAsync(days));
            }
            catch (Exception ex)
            {
                loggerFactory.CreateLogger("Admin").LogError("GetEmailStats failed: {Error}", ex.Message);
                return TypedResults.Problem("Failed to fetch email stats from Brevo.", statusCode: StatusCodes.Status502BadGateway);
            }
        }

        private static long DirectorySize(string path)
        {
            if (!Directory.Exists(path)) return 0;
            return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
        }

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                var group = app.MapGroup("/api/admin").RequireAuthorization(Policies.Admin);
                group.MapGet("stats", GetStats);
                group.MapGet("stats/history", GetStatsHistory);
                group.MapGet("roles", GetRoles);
                group.MapGet("email-stats", GetEmailStats);
            }
        }
    }
}
