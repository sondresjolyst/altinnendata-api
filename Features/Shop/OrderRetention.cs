using Microsoft.EntityFrameworkCore;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;

namespace altinnendata_api.Features.Shop
{
    /// <summary>
    /// Deletes the orders there is no reason to keep: failed orders, cancelled orders that were never captured and
    /// orders made while Vipps was in test mode, 90 days after they were created. An order that still holds a build is kept until the build is released.
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
                    && (o.Status == OrderStatus.Failed
                        || (o.Status == OrderStatus.Cancelled && o.CapturedInOre == 0)
                        || o.IsTest)
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

    /// <summary>
    /// Removes the buyer's name, email, phone and address from a sale five years after the end of the financial
    /// year it belongs to. The financial year is the calendar year in Oslo time of the latest money date of the
    /// order: the capture (or the creation when never captured), the last update of a partly captured order,
    /// and every refund. A sale in 2026 is anonymised from 1 January 2032, and one refunded in 2029 from 1 January 2035. The amounts, dates,
    /// build, references, invoice number, terms, payments and refunds stay for the accounts.
    /// </summary>
    public static class OrderAnonymisation
    {
        public const int KeepYears = 5;
        public const string AnonymisedName = "Anonymisert";

        /// <summary>The most orders one run anonymises. The rest go in the next run.</summary>
        public const int BatchSize = 500;

        private const int MaxSaveAttempts = 3;

        /// <summary>Orders dated before this instant are due. It is 1 January, <see cref="KeepYears"/> years back, in Oslo time.</summary>
        public static DateTime CutoffAt(DateTime now)
        {
            var year = OsloTime.FromUtc(now).Year - KeepYears;
            return TimeZoneInfo.ConvertTimeToUtc(new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Unspecified), OsloTime.Zone);
        }

        /// <summary>Anonymises one batch of due orders and returns how many were changed.</summary>
        public static async Task<int> AnonymiseAsync(ApplicationDbContext db, DateTime now, CancellationToken ct)
        {
            var cutoff = CutoffAt(now);
            for (var attempt = 1; ; attempt++)
            {
                var due = await db.Orders
                    .Where(o => o.AnonymisedAt == null
                        && (o.CapturedAt ?? o.CreatedAt) < cutoff
                        && (o.CapturedInOre == 0 || o.CapturedAt != null || o.UpdatedAt < cutoff)
                        && (o.RefundedAt == null || o.RefundedAt < cutoff)
                        && !o.Refunds.Any(r => r.RefundedAt >= cutoff))
                    .OrderBy(o => o.Id)
                    .Take(BatchSize)
                    .ToListAsync(ct);

                if (due.Count == 0) return 0;

                foreach (var order in due) Clear(order, now);
                try
                {
                    await db.SaveChangesAsync(ct);
                    return due.Count;
                }
                catch (DbUpdateConcurrencyException) when (attempt < MaxSaveAttempts)
                {
                    // A webhook changed an order after the read. Read the batch again.
                    db.ChangeTracker.Clear();
                }
            }
        }

        private static void Clear(Order order, DateTime now)
        {
            order.BuyerName = AnonymisedName;
            order.BuyerEmail = "";
            order.BuyerPhone = "";
            order.StreetAddress = null;
            order.PostalCode = null;
            order.City = null;
            order.PickupNote = null;
            order.ShippingNote = null;
            order.TermsAcceptedIp = null;
            order.AnonymisedAt = now;
            order.UpdatedAt = now;
        }
    }

    /// <summary>Runs <see cref="OrderRetention"/> and <see cref="OrderAnonymisation"/> at start and then once a day.</summary>
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

        /// <summary>One run. Each step logs its own failure, so one failing does not skip the other. The next run tries again.</summary>
        public async Task RunOnceAsync(CancellationToken ct)
        {
            var now = time.GetUtcNow().UtcDateTime;
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var deleted = await OrderRetention.PurgeAsync(db, now, ct);
                if (deleted > 0)
                    log.LogInformation("Deleted {Count} failed, uncaptured cancelled or test orders older than {Days} days", deleted, OrderRetention.KeepFor.TotalDays);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Deleting old failed, uncaptured cancelled and test orders failed, it is tried again tomorrow");
            }

            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var anonymised = await OrderAnonymisation.AnonymiseAsync(db, now, ct);
                if (anonymised > 0)
                    log.LogInformation("Anonymised the buyer details of {Count} orders from financial years more than {Years} years ago", anonymised, OrderAnonymisation.KeepYears);
                if (anonymised == OrderAnonymisation.BatchSize)
                    log.LogWarning("A full batch of {Count} orders was anonymised, so more orders are probably due and follow in the next run", anonymised);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Anonymising the buyer details of old orders failed, it is tried again tomorrow");
            }
        }
    }
}
