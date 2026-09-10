using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Services;

namespace altinnendata_api.Features.Admin
{
    /// <summary>Admin dashboard: aggregate stats, daily history, roles, Brevo email stats.</summary>
    public static class AdminDashboard
    {
        public static async Task<IResult> GetStats(ApplicationDbContext db, IConfiguration config, ILoggerFactory loggerFactory, CancellationToken ct)
        {
            var options = config.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();
            var imagesPath = Path.GetFullPath(options.ImagesPath);

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

        public static async Task<IResult> GetStatsHistory(ApplicationDbContext db, CancellationToken ct)
        {
            var snapshots = await StatsSnapshotService.GetHistoryAsync(db, ct);

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
