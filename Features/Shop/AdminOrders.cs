using Microsoft.EntityFrameworkCore;
using SjolystInnovation.AppKit.Vipps;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;

namespace altinnendata_api.Features.Shop
{
    /// <summary>
    /// The admin's orders: the list, one order, capture, cancel, refund and the invoice number.
    /// The Vipps webhooks move the same orders, so every action ends in the state the matching webhook
    /// would leave, and only the side that makes a transition sends its email.
    /// </summary>
    public static class AdminOrders
    {
        /// <summary>How many times a save that lost a race with a webhook is applied again on a fresh read.</summary>
        private const int MaxSaveAttempts = 3;

        public static async Task<IResult> List(ApplicationDbContext db, CancellationToken ct)
        {
            var orders = await db.Orders.AsNoTracking()
                .OrderByDescending(o => o.CreatedAt)
                .ThenByDescending(o => o.Id)
                .ToListAsync(ct);

            return TypedResults.Ok(orders.Select(AdminOrderSummaryDto.From).ToList());
        }

        public static async Task<IResult> Get(int id, ApplicationDbContext db, CancellationToken ct)
        {
            var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, ct);
            return order == null ? TypedResults.NotFound() : TypedResults.Ok(AdminOrderDto.From(order));
        }

        public static async Task<IResult> Capture(int id, ApplicationDbContext db, IVippsClient vipps, ILogger<AdminOrderDto> log, CancellationToken ct)
        {
            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (order == null) return TypedResults.NotFound();
            if (order.Status != OrderStatus.Reserved) return WrongStatus(order, "Only a reserved order can be captured.");

            VippsPayment payment;
            try
            {
                payment = await vipps.CapturePaymentAsync(order.Reference, order.TotalInOre, $"capture-{order.Reference}", order.IsTest, ct);
            }
            catch (Exception ex) when (IsProviderFailure(ex, ct))
            {
                return ProviderUnavailable(log, ex, "capture", order);
            }

            // Vipps has taken the money, so an aborted admin request must not stop the save.
            var buildNotSold = false;
            var (saved, fresh) = await SaveAsync(db, order, OrderStatus.Reserved, OrderStatus.Paid, (o, build) =>
            {
                buildNotSold = false;
                o.CapturedInOre = Math.Clamp(Math.Max(o.CapturedInOre, payment.CapturedAmountInOre), 0, o.TotalInOre);
                if (o.CapturedInOre < o.TotalInOre) return;

                o.Status = OrderStatus.Paid;
                o.CapturedAt = DateTime.UtcNow;
                if (build != null) buildNotSold = !BuildHold.MarkSold(build, o.Id, DateOnly.FromDateTime(DateTime.UtcNow));
            }, CancellationToken.None);

            if (saved == Saved.Moved)
            {
                log.LogWarning("Vipps captured {Captured} øre of order {OrderId}, but the order is now {Status}",
                    payment.CapturedAmountInOre, fresh.Id, fresh.Status);
                return WrongStatus(fresh, "The order changed while it was being captured.");
            }
            if (saved == Saved.Applied && buildNotSold)
                log.LogWarning("Order {OrderId} is paid but no longer holds build {BuildId}, so the build was not marked sold",
                    fresh.Id, fresh.PcBuildId);
            if (fresh.Status != OrderStatus.Paid)
            {
                log.LogError("Admin capture of order {OrderId} captured {Captured} of {Total} øre, the order stays {Status}",
                    fresh.Id, fresh.CapturedInOre, fresh.TotalInOre, fresh.Status);
                return ShopProblems.Code(StatusCodes.Status502BadGateway, "captureIncomplete",
                    "Vipps did not confirm the full capture. Check the payment in the Vipps portal.");
            }

            log.LogInformation("Order {OrderId} captured by an admin", fresh.Id);
            return TypedResults.Ok(AdminOrderDto.From(fresh));
        }

        public static async Task<IResult> Cancel(int id, ApplicationDbContext db, IVippsClient vipps, IOrderNotifier notifier, ILogger<AdminOrderDto> log, CancellationToken ct)
        {
            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (order == null) return TypedResults.NotFound();
            if (order.Status != OrderStatus.Reserved) return WrongStatus(order, "Only a reserved order can be cancelled.");
            // The same case the cancel webhook leaves alone: the buyer paid part, so a person sorts it out in Vipps.
            if (order.CapturedInOre > 0) return WrongStatus(order, "A partly captured order cannot be cancelled here. Handle it in the Vipps portal.");

            try
            {
                await vipps.CancelPaymentAsync(order.Reference, $"cancel-{order.Reference}", order.IsTest, ct);
            }
            catch (Exception ex) when (IsProviderFailure(ex, ct))
            {
                return ProviderUnavailable(log, ex, "cancel", order);
            }

            // Vipps has released the reservation, so an aborted admin request must not stop the save or the email.
            var (saved, fresh) = await SaveAsync(db, order, OrderStatus.Reserved, OrderStatus.Cancelled, (o, build) =>
            {
                o.Status = OrderStatus.Cancelled;
                o.CancelledAt = DateTime.UtcNow;
                if (build != null) BuildHold.Release(build, o.Id);
            }, CancellationToken.None);

            if (saved == Saved.Moved) return WrongStatus(fresh, "The order changed while it was being cancelled.");
            if (saved == Saved.Applied) await notifier.OrderCancelledAsync(fresh.Id, CancellationToken.None);

            log.LogInformation("Order {OrderId} cancelled by an admin", fresh.Id);
            return TypedResults.Ok(AdminOrderDto.From(fresh));
        }

        public static async Task<IResult> Refund(int id, ApplicationDbContext db, IVippsClient vipps, IOrderNotifier notifier, ILogger<AdminOrderDto> log, CancellationToken ct)
        {
            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (order == null) return TypedResults.NotFound();
            if (order.Status != OrderStatus.Paid) return WrongStatus(order, "Only a paid order can be refunded.");

            VippsPayment payment;
            try
            {
                payment = await vipps.RefundPaymentAsync(order.Reference, order.TotalInOre, $"refund-{order.Reference}", order.IsTest, ct);
            }
            catch (Exception ex) when (IsProviderFailure(ex, ct))
            {
                return ProviderUnavailable(log, ex, "refund", order);
            }

            // The build stays sold. Whether it goes back on sale is the admin's own choice.
            // Vipps has paid the money back, so an aborted admin request must not stop the save or the email.
            var (saved, fresh) = await SaveAsync(db, order, OrderStatus.Paid, OrderStatus.Refunded, (o, _) =>
            {
                o.RefundedInOre = Math.Clamp(Math.Max(o.RefundedInOre, payment.RefundedAmountInOre), 0, o.TotalInOre);
                if (o.RefundedInOre < o.TotalInOre) return;

                o.Status = OrderStatus.Refunded;
                o.RefundedAt = DateTime.UtcNow;
            }, CancellationToken.None);

            if (saved == Saved.Moved)
            {
                log.LogWarning("Vipps refunded {Refunded} øre of order {OrderId}, but the order is now {Status}",
                    payment.RefundedAmountInOre, fresh.Id, fresh.Status);
                return WrongStatus(fresh, "The order changed while it was being refunded.");
            }
            if (fresh.Status != OrderStatus.Refunded)
            {
                log.LogError("Admin refund of order {OrderId} refunded {Refunded} of {Total} øre, the order stays {Status}",
                    fresh.Id, fresh.RefundedInOre, fresh.TotalInOre, fresh.Status);
                return ShopProblems.Code(StatusCodes.Status502BadGateway, "refundIncomplete",
                    "Vipps did not confirm the full refund. Check the payment in the Vipps portal.");
            }

            if (saved == Saved.Applied) await notifier.OrderRefundedAsync(fresh.Id, CancellationToken.None);

            log.LogInformation("Order {OrderId} refunded by an admin", fresh.Id);
            return TypedResults.Ok(AdminOrderDto.From(fresh));
        }

        public static async Task<IResult> SetInvoiceNumber(int id, InvoiceNumberRequest req, ApplicationDbContext db, CancellationToken ct)
        {
            var number = string.IsNullOrWhiteSpace(req.InvoiceNumber) ? null : req.InvoiceNumber.Trim();
            var now = DateTime.UtcNow;

            // A single UPDATE of the two columns, so it never overwrites a webhook's change to the order.
            var rows = await db.Orders
                .Where(o => o.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.InvoiceNumber, number)
                    .SetProperty(o => o.UpdatedAt, now), ct);
            if (rows == 0) return TypedResults.NotFound();

            var order = await db.Orders.AsNoTracking().FirstAsync(o => o.Id == id, ct);
            return TypedResults.Ok(AdminOrderDto.From(order));
        }

        private enum Saved
        {
            /// <summary>This action made the transition.</summary>
            Applied,

            /// <summary>A webhook made the same transition first.</summary>
            AlreadyThere,

            /// <summary>A webhook moved the order somewhere else first.</summary>
            Moved
        }

        /// <summary>
        /// Applies the result of the Vipps call to the order and its build and saves them. A webhook that changed
        /// either after the read fails the save. Then the order is read again: a webhook that already reached
        /// <paramref name="to"/> wins, an order still in <paramref name="from"/> gets the change again, and any
        /// other state is left as the webhook made it.
        /// </summary>
        private static async Task<(Saved, Order)> SaveAsync(
            ApplicationDbContext db, Order order, OrderStatus from, OrderStatus to, Action<Order, PcBuild?> apply, CancellationToken ct)
        {
            var build = await LoadBuildAsync(db, order, ct);
            for (var attempt = 1; ; attempt++)
            {
                apply(order, build);
                order.UpdatedAt = DateTime.UtcNow;
                try
                {
                    await db.SaveChangesAsync(ct);
                    return (Saved.Applied, order);
                }
                catch (DbUpdateConcurrencyException)
                {
                    db.ChangeTracker.Clear();
                    order = await db.Orders.FirstAsync(o => o.Id == order.Id, ct);
                    if (order.Status == to) return (Saved.AlreadyThere, order);
                    if (order.Status != from || attempt == MaxSaveAttempts) return (Saved.Moved, order);
                    build = await LoadBuildAsync(db, order, ct);
                }
            }
        }

        private static async Task<PcBuild?> LoadBuildAsync(ApplicationDbContext db, Order order, CancellationToken ct) =>
            order.PcBuildId is int buildId ? await db.PcBuilds.FindAsync([buildId], ct) : null;

        /// <summary>Vipps could not be reached or answered with an error. HttpClient reports its own timeout as a cancellation.</summary>
        private static bool IsProviderFailure(Exception ex, CancellationToken ct) =>
            ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested);

        /// <summary>
        /// Vipps timed out or refused. After a timeout the change may still have happened in Vipps, so the order is
        /// left as it was and the admin checks the portal. A retry uses the same idempotency key and is safe.
        /// </summary>
        private static IResult ProviderUnavailable(ILogger log, Exception ex, string action, Order order)
        {
            log.LogError(ex, "Vipps did not confirm the {Action} of the payment of order {OrderId}, the order was left as it was", action, order.Id);
            return ShopProblems.Code(StatusCodes.Status502BadGateway, "paymentProviderUnavailable",
                "Vipps did not confirm the change. Check the payment in the Vipps portal before you try again.");
        }

        private static IResult WrongStatus(Order order, string message) =>
            ShopProblems.Code(StatusCodes.Status409Conflict, "wrongStatus", $"{message} The order is {order.Status}.");

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                var admin = app.MapGroup("/api/admin/shop/orders").RequireAuthorization(Policies.Admin);
                admin.MapGet("", List);
                admin.MapGet("{id:int}", Get);
                admin.MapPost("{id:int}/capture", Capture);
                admin.MapPost("{id:int}/cancel", Cancel);
                admin.MapPost("{id:int}/refund", Refund);
                admin.MapPut("{id:int}/invoice-number", SetInvoiceNumber).WithValidation<InvoiceNumberRequest>();
            }
        }
    }
}
