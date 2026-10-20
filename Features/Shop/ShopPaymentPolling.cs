using Microsoft.EntityFrameworkCore;
using SjolystInnovation.AppKit.Vipps;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;

namespace altinnendata_api.Features.Shop
{
    /// <summary>
    /// The fallback Vipps asks for when a webhook is lost: reads the payment of every open order from Vipps and applies
    /// what it shows through <see cref="ShopWebhookHandler.ApplyAsync"/>, with the same transitions, holds and emails.
    /// Open means Reserved, or Pending for longer than <see cref="PendingAfter"/> since the current Vipps payment started,
    /// so a buyer still in Vipps is left alone. It also tells the owner once when a payment link ran out unpaid.
    /// A webhook for the same change at the same time is safe: the order's xmin lets only one save win, and only the
    /// side that saves sends the email.
    /// </summary>
    public class ShopPaymentPolling(
        ApplicationDbContext db,
        IVippsClient vipps,
        ShopWebhookHandler handler,
        IOrderNotifier notifier,
        ILogger<ShopPaymentPolling> log)
    {
        public static readonly TimeSpan PendingAfter = TimeSpan.FromMinutes(15);

        /// <summary>The most orders one run reads from Vipps. The next run goes on after the last one.</summary>
        public const int BatchSize = 25;

        /// <summary>
        /// Checks one batch of open orders with an id above <paramref name="afterId"/>, oldest first. Returns the id of
        /// the last order checked, or 0 when the batch reached the end, so the next run starts from the beginning.
        /// A Vipps failure other than a refusal for one payment ends the run, and the next run tries again.
        /// </summary>
        public async Task<int> RunAsync(int afterId, DateTime now, CancellationToken ct)
        {
            var pendingBefore = now - PendingAfter;
            var ids = await db.Orders.AsNoTracking()
                .Where(o => o.Id > afterId
                    && (o.Status == OrderStatus.Reserved
                        || (o.Status == OrderStatus.Pending && o.CreatedAt < pendingBefore
                            && !db.OrderPayments.Any(p => p.VippsReference == o.VippsReference && p.CreatedAt >= pendingBefore))))
                .OrderBy(o => o.Id)
                .Select(o => o.Id)
                .Take(BatchSize)
                .ToListAsync(ct);

            foreach (var id in ids)
            {
                if (!await CheckAsync(id, ct)) return id - 1;
            }
            return ids.Count < BatchSize ? 0 : ids[^1];
        }

        /// <summary>
        /// Closes every payment link that ran out at <paramref name="now"/> without a payment and tells the owner, once
        /// per link, while the order still holds its build. An order still Pending is left until its payment ends.
        /// Returns how many links were closed.
        /// </summary>
        public async Task<int> CloseExpiredLinksAsync(DateTime now, CancellationToken ct)
        {
            var ids = await db.Orders.AsNoTracking()
                .Where(o => o.PaymentLinkTokenHash != null && o.PaymentLinkUsedAt == null && o.PaymentLinkExpiresAt <= now
                    && (o.Status == OrderStatus.Failed || o.Status == OrderStatus.Cancelled))
                .OrderBy(o => o.Id)
                .Select(o => o.Id)
                .Take(BatchSize)
                .ToListAsync(ct);

            var closed = 0;
            foreach (var id in ids)
            {
                db.ChangeTracker.Clear();
                var order = await db.Orders.FirstAsync(o => o.Id == id, ct);
                order.PaymentLinkTokenHash = null;
                order.UpdatedAt = DateTime.UtcNow;
                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    // The buyer started a payment or the admin sent a new link meanwhile. The next run looks again.
                    continue;
                }
                closed++;
                if (!await PaymentLink.HoldsBuildAsync(db, order, ct)) continue;

                log.LogWarning("Payment polling: the payment link of order {OrderId} ran out unpaid, the build stays held for the owner to sort out", id);
                try
                {
                    await notifier.PaymentLinkExpiredAsync(id, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Payment polling: the email about the expired payment link of order {OrderId} failed", id);
                }
            }
            db.ChangeTracker.Clear();
            return closed;
        }

        /// <summary>Reads one order's payment and applies it. Returns false when Vipps could not be reached.</summary>
        private async Task<bool> CheckAsync(int orderId, CancellationToken ct)
        {
            db.ChangeTracker.Clear();
            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (order is null || order.Status is not (OrderStatus.Pending or OrderStatus.Reserved)) return true;

            VippsPayment payment;
            try
            {
                payment = await vipps.GetPaymentAsync(order.VippsReference, order.IsTest, ct);
            }
            catch (HttpRequestException ex) when (ShopVipps.IsRejection(ex))
            {
                log.LogWarning(ex, "Payment polling: Vipps refused to show the payment of order {OrderId} with {Status}, it is tried again next run",
                    order.Id, (int)ex.StatusCode!.Value);
                return true;
            }
            catch (Exception ex) when (ShopVipps.IsProviderFailure(ex, ct))
            {
                log.LogWarning(ex, "Payment polling: could not read the payment of order {OrderId} from Vipps, the run stops and the next run tries again", order.Id);
                return false;
            }

            foreach (var change in ChangesOf(order, payment, DateTime.UtcNow))
            {
                if (!await ApplyAsync(order, change, payment, ct)) break;
                if (order.Status is not (OrderStatus.Pending or OrderStatus.Reserved or OrderStatus.Paid)) break;
            }
            return true;
        }

        /// <summary>Applies and saves one change, then sends its emails. Returns false when nothing more should be applied.</summary>
        private async Task<bool> ApplyAsync(Order order, PaymentChange change, VippsPayment payment, CancellationToken ct)
        {
            var outcome = await handler.ApplyAsync(order, change, payment, ct);
            if (outcome.Kind != VippsWebhookOutcomeKind.Applied)
            {
                db.ChangeTracker.Clear();
                return false;
            }

            if (!db.ChangeTracker.HasChanges()) return true;
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                // A webhook or an admin changed the order or its build meanwhile. Whoever saved sends the emails,
                // and the next run reads the order again.
                log.LogInformation("Payment polling: order {OrderId} changed while {Name} was applied, it is checked again next run", order.Id, change.Name);
                db.ChangeTracker.Clear();
                return false;
            }

            log.LogWarning("Payment polling: applied {Name} to order {OrderId} from the payment, its webhook did not arrive", change.Name, order.Id);
            if (outcome.AfterCommit is { } afterCommit)
            {
                try
                {
                    await afterCommit(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Payment polling: the emails after {Name} on order {OrderId} failed", change.Name, order.Id);
                }
            }
            return true;
        }

        /// <summary>
        /// The changes a payment shows, in the order Vipps makes them. A payment that is still AUTHORIZED carries its
        /// captures, refunds and cancellation as amounts. ABORTED, EXPIRED and TERMINATED are states of their own.
        /// CREATED shows nothing yet.
        /// </summary>
        public static IReadOnlyList<PaymentChange> ChangesOf(Order order, VippsPayment payment, DateTime now)
        {
            PaymentChange Change(string name, int? amount) =>
                new(name, amount, now, $"poll:{order.VippsReference}:{name}:{amount}", RefundSource.Polling);

            switch (payment.State)
            {
                case "ABORTED":
                case "EXPIRED":
                case "TERMINATED":
                    return [Change(payment.State, null)];
                case "AUTHORIZED":
                    if (payment.CapturedAmountInOre > 0)
                    {
                        List<PaymentChange> changes = [Change("CAPTURED", payment.CapturedAmountInOre)];
                        if (payment.RefundedAmountInOre > 0) changes.Add(Change("REFUNDED", payment.RefundedAmountInOre));
                        return changes;
                    }
                    if (payment.CancelledAmountInOre > 0) return [Change("CANCELLED", null)];
                    // Vipps authorizes the amount the payment was created with, which is the order's total.
                    return [Change("AUTHORIZED", order.TotalInOre)];
                default:
                    return [];
            }
        }
    }

    /// <summary>Runs <see cref="ShopPaymentPolling"/> every <see cref="Interval"/>, after a first wait of the same length.</summary>
    public sealed class ShopPaymentPollingService(
        IServiceScopeFactory scopes,
        TimeProvider time,
        ILogger<ShopPaymentPollingService> log) : BackgroundService
    {
        public static readonly TimeSpan Interval = TimeSpan.FromMinutes(3);

        private int _afterId;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(Interval, time);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunOnceAsync(stoppingToken);
        }

        /// <summary>One run. A failure is logged and the next run tries again.</summary>
        public async Task RunOnceAsync(CancellationToken ct)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var polling = scope.ServiceProvider.GetRequiredService<ShopPaymentPolling>();
                var now = time.GetUtcNow().UtcDateTime;
                _afterId = await polling.RunAsync(_afterId, now, ct);
                await polling.CloseExpiredLinksAsync(now, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Payment polling failed, it is tried again in {Minutes} minutes", Interval.TotalMinutes);
            }
        }
    }
}
