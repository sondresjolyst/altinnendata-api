using Microsoft.EntityFrameworkCore;
using altinnendata_api.Models;
using altinnendata_api.Models.Admin;

namespace altinnendata_api.Services
{
    /// <summary>
    /// Freezes one row of site totals per completed UTC day. Today is never frozen — it is
    /// computed live on read, so same-day additions and removals are always reflected.
    /// </summary>
    public class StatsSnapshotService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<StatsSnapshotService> _logger;

        public StatsSnapshotService(IServiceScopeFactory scopeFactory, ILogger<StatsSnapshotService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var added = await EnsureUpToDateAsync(db, stoppingToken);
                    if (added > 0)
                        _logger.LogInformation("Stats snapshot froze {Count} completed day(s)", added);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Stats snapshot update failed");
                }

                try { await Task.Delay(Interval, stoppingToken); }
                catch (TaskCanceledException) { break; }
            }
        }

        /// <summary>
        /// Freezes a row for each completed day (through yesterday) that is missing, sampling the
        /// current live totals. Existing rows are never touched. Returns the number of rows added.
        /// </summary>
        public static async Task<int> EnsureUpToDateAsync(ApplicationDbContext db, CancellationToken ct = default)
        {
            var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);

            var last = await db.DailyStatSnapshots
                .OrderByDescending(s => s.Date)
                .FirstOrDefaultAsync(ct);

            var from = last == null ? yesterday : last.Date.AddDays(1);
            if (from > yesterday) return 0;

            var live = await SampleAsync(db, ct);

            var rows = new List<DailyStatSnapshot>();
            for (var date = from; date <= yesterday; date = date.AddDays(1))
            {
                rows.Add(new DailyStatSnapshot
                {
                    Date = date,
                    TotalUsers = live.TotalUsers,
                    PublishedBuilds = live.PublishedBuilds,
                    DraftBuilds = live.DraftBuilds,
                    CatalogParts = live.CatalogParts,
                    ContentImages = live.ContentImages
                });
            }

            db.DailyStatSnapshots.AddRange(rows);
            await db.SaveChangesAsync(ct);
            return rows.Count;
        }

        /// <summary>
        /// Returns the frozen daily series plus a live, non-persisted row for today.
        /// </summary>
        public static async Task<List<DailyStatSnapshot>> GetHistoryAsync(ApplicationDbContext db, CancellationToken ct = default)
        {
            await EnsureUpToDateAsync(db, ct);

            var snapshots = await db.DailyStatSnapshots.OrderBy(s => s.Date).ToListAsync(ct);

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var live = await SampleAsync(db, ct);
            snapshots.Add(new DailyStatSnapshot
            {
                Date = today,
                TotalUsers = live.TotalUsers,
                PublishedBuilds = live.PublishedBuilds,
                DraftBuilds = live.DraftBuilds,
                CatalogParts = live.CatalogParts,
                ContentImages = live.ContentImages
            });
            return snapshots;
        }

        private static async Task<DailyStatSnapshot> SampleAsync(ApplicationDbContext db, CancellationToken ct) => new()
        {
            TotalUsers = await db.Users.CountAsync(u => !u.IsDeleted, ct),
            PublishedBuilds = await db.PcBuilds.CountAsync(b => b.Published, ct),
            DraftBuilds = await db.PcBuilds.CountAsync(b => !b.Published, ct),
            CatalogParts = await db.ComponentParts.CountAsync(ct),
            ContentImages = await db.ContentImages.CountAsync(ct)
        };
    }
}
