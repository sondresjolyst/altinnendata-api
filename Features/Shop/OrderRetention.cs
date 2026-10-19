using Microsoft.EntityFrameworkCore;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;

namespace altinnendata_api.Features.Shop
{
    /// <summary>
    /// Deletes the orders there is no reason to keep: failed orders and orders made while Vipps was in test mode,
    /// 90 days after they were created. An order that still holds a build is kept until the build is released.
    /// Sales are kept for the accounts.
    /// </summary>
    public static class OrderRetention
    {
        public static readonly TimeSpan KeepFor = TimeSpan.FromDays(90);

        /// <summary>The most orders one run deletes. The rest go in the next run.</summary>
        public const int BatchSize = 500;

        /// <summary>Deletes one batch of expired orders and returns how many were deleted.</summary>
        public static async Task<int> PurgeAsync(ApplicationDbContext db, DateTime now, CancellationToken ct)
        {
            var cutoff = now - KeepFor;
            var expired = await db.Orders
                .Where(o => o.CreatedAt < cutoff
                    && (o.Status == OrderStatus.Failed || o.IsTest)
                    && !db.PcBuilds.Any(b => b.HeldByOrderId == o.Id))
                .OrderBy(o => o.Id)
                .Take(BatchSize)
                .ToListAsync(ct);

            if (expired.Count == 0) return 0;

            db.Orders.RemoveRange(expired);
            await db.SaveChangesAsync(ct);
            return expired.Count;
        }
    }

    /// <summary>Runs <see cref="OrderRetention"/> at start and then once a day.</summary>
    public sealed class OrderRetentionService(
        IServiceScopeFactory scopes,
        TimeProvider time,
        ILogger<OrderRetentionService> log) : BackgroundService
    {
        public static readonly TimeSpan Interval = TimeSpan.FromDays(1);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval, time);
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        /// <summary>One run. A failure is logged and the next run tries again.</summary>
        public async Task RunOnceAsync(CancellationToken ct)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var deleted = await OrderRetention.PurgeAsync(db, time.GetUtcNow().UtcDateTime, ct);
                if (deleted > 0)
                    log.LogInformation("Deleted {Count} failed or test orders older than {Days} days", deleted, OrderRetention.KeepFor.TotalDays);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Deleting old failed and test orders failed, it is tried again tomorrow");
            }
        }
    }
}
