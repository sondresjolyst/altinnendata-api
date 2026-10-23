using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SjolystInnovation.AppKit.Vipps;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;

namespace altinnendata_api.Features.Shop
{
    /// <summary>
    /// A new way to pay after Vipps ended the reservation of an order on its own. The admin sends the buyer a link to
    /// the shop's pay page. The buyer starts a normal Vipps payment from there, as many times as needed while the link
    /// is valid, and the build stays held for the order the whole time. Only a hash of the link's secret is stored.
    /// </summary>
    public static partial class PaymentLink
    {
        /// <summary>How long a payment link works.</summary>
        public static readonly TimeSpan Validity = TimeSpan.FromDays(3);

        [GeneratedRegex("^[A-Za-z0-9_-]{43}$")]
        private static partial Regex TokenPattern();

        /// <summary>The secret in the link: 32 random bytes, base64url without padding.</summary>
        public static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

        /// <summary>The SHA-256 hash of the secret as lowercase hex, which is what the order stores.</summary>
        public static string HashOf(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(token)));

        /// <summary>The Vipps reference of the order's <paramref name="number"/>th payment. The first is the order number itself.</summary>
        public static string VippsReferenceOf(Order order, int number) =>
            number <= 1 ? order.Reference : $"{order.Reference}-{number}";

        /// <summary>The order's current Vipps payment was started from a payment link.</summary>
        public static bool IsLinkPayment(Order order) => order.VippsReference != order.Reference;

        /// <summary>
        /// A payment from the link was authorized: the order is reserved again, the link is used up, and the buyer gets
        /// an order confirmation as for a new order.
        /// </summary>
        public static void Authorized(Order order, DateTime at)
        {
            order.PaymentLinkUsedAt = at;
            order.PaymentLinkTokenHash = null;
            order.ReservationEndedAt = null;
            order.CancelledAt = null;
            order.ConfirmationSentAt = null;
            order.ConfirmationAttempts = 0;
            order.ConfirmationAttemptedAt = null;
        }

        /// <summary>
        /// Why the admin cannot send a payment link now, as the code the endpoint would answer with: <c>wrongStatus</c>
        /// unless Vipps ended the reservation and the order is failed or cancelled, <c>buildNotHeld</c> when it no longer
        /// holds the build. Null when it can.
        /// </summary>
        public static string? BlockedReasonOf(Order o, bool holdsBuild) =>
            o.AnonymisedAt != null ? "anonymised"
            : o.ReservationEndedAt == null || o.Status is not (OrderStatus.Failed or OrderStatus.Cancelled) ? "wrongStatus"
            : !holdsBuild ? "buildNotHeld"
            : null;

        /// <summary>
        /// The link can be used at <paramref name="now"/>: it was neither used nor replaced and has not run out.
        /// Whether the order still holds its build is checked against the database.
        /// </summary>
        public static bool IsOpen(Order o, DateTime now) =>
            o.PaymentLinkTokenHash != null && o.PaymentLinkUsedAt == null && o.PaymentLinkExpiresAt > now;

        public static Task<bool> HoldsBuildAsync(ApplicationDbContext db, Order o, CancellationToken ct) =>
            o.PcBuildId is int buildId
                ? db.PcBuilds.AnyAsync(b => b.Id == buildId && b.HeldByOrderId == o.Id && b.Availability == BuildAvailability.Reserved, ct)
                : Task.FromResult(false);

        /// <summary>
        /// The order whose open link has <paramref name="token"/>, while it still holds its build. Null for a token
        /// that is malformed, unknown, used, replaced or expired, so every one of them gets the same answer.
        /// </summary>
        private static async Task<Order?> FindOpenAsync(ApplicationDbContext db, string token, DateTime now, bool track, CancellationToken ct)
        {
            if (!TokenPattern().IsMatch(token)) return null;

            var hash = HashOf(token);
            var orders = track ? db.Orders : db.Orders.AsNoTracking();
            var order = await orders.FirstOrDefaultAsync(o => o.PaymentLinkTokenHash == hash, ct);
            if (order == null || !IsOpen(order, now)) return null;
            return await HoldsBuildAsync(db, order, ct) ? order : null;
        }

        /// <summary>Sends the buyer a new payment link, which replaces any link sent before.</summary>
        public static async Task<IResult> Send(int id, ApplicationDbContext db, IOrderNotifier notifier, ILogger<PayLinkDto> log, CancellationToken ct)
        {
            string token;
            var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (order == null) return TypedResults.NotFound();

            for (var attempt = 1; ; attempt++)
            {
                var reason = BlockedReasonOf(order, await HoldsBuildAsync(db, order, ct));
                if (reason == "anonymised")
                    return ShopProblems.Code(StatusCodes.Status409Conflict, reason, $"Order {order.Reference} is anonymised and can no longer be changed.");
                if (reason == "wrongStatus")
                    return ShopProblems.Code(StatusCodes.Status409Conflict, reason,
                        $"Only an order whose reservation Vipps ended can get a payment link. The order is {order.Status}.");
                if (reason == "buildNotHeld")
                    return ShopProblems.Code(StatusCodes.Status409Conflict, reason, "The order no longer holds the build.");

                // To the microsecond Postgres keeps, so the answer shows the times as stored.
                var now = DateTime.UtcNow;
                now = now.AddTicks(-(now.Ticks % 10));
                token = NewToken();
                order.PaymentLinkTokenHash = HashOf(token);
                order.PaymentLinkSentAt = now;
                order.PaymentLinkExpiresAt = now + Validity;
                order.PaymentLinkUsedAt = null;
                order.UpdatedAt = now;
                try
                {
                    await db.SaveChangesAsync(ct);
                    break;
                }
                catch (DbUpdateConcurrencyException) when (attempt < 3)
                {
                    // A webhook or the buyer changed the order after the read. Check it again on a fresh read.
                    db.ChangeTracker.Clear();
                    order = await db.Orders.FirstAsync(o => o.Id == id, ct);
                }
                catch (DbUpdateConcurrencyException)
                {
                    db.ChangeTracker.Clear();
                    return ShopProblems.Code(StatusCodes.Status409Conflict, "wrongStatus", "The order changed while the payment link was being sent.");
                }
            }

            // The link is saved, so an aborted admin request must not stop the email.
            await notifier.PaymentLinkSentAsync(order.Id, token, CancellationToken.None);
            log.LogInformation("Order {OrderId} got a new payment link from an admin, valid until {ExpiresAt:o}", order.Id, order.PaymentLinkExpiresAt);
            return TypedResults.Ok(await AdminOrderDto.LoadAsync(db, order, CancellationToken.None));
        }

        /// <summary>What the pay page shows. No buyer data.</summary>
        public static async Task<IResult> Get(string token, ApplicationDbContext db, CancellationToken ct)
        {
            var order = await FindOpenAsync(db, token, DateTime.UtcNow, false, ct);
            return order == null
                ? NotValid()
                : TypedResults.Ok(new PayLinkDto(order.Reference, order.BuildTitle, order.TotalInOre, order.PaymentLinkExpiresAt!.Value, order.Locale));
        }

        /// <summary>
        /// Starts a new Vipps payment for the order, with the next Vipps reference and the same amount and lines. The
        /// order is Pending until Vipps reports the payment. One that ends unpaid leaves the order as it was before, so the
        /// buyer can try again while the link is valid.
        /// </summary>
        public static async Task<IResult> Pay(string token, ApplicationDbContext db, IVippsClient vipps, IConfiguration config, ILogger<PayLinkDto> log, CancellationToken ct)
        {
            var order = await FindOpenAsync(db, token, DateTime.UtcNow, true, ct);
            if (order == null) return NotValid();
            if (order.Status == OrderStatus.Pending) return PaymentInProgress();
            if (order.Status is not (OrderStatus.Failed or OrderStatus.Cancelled))
                return ShopProblems.Code(StatusCodes.Status409Conflict, "wrongStatus", $"The order is {order.Status}.");

            var number = Math.Max(2, await db.OrderPayments.CountAsync(p => p.OrderId == order.Id, ct) + 1);
            var reference = VippsReferenceOf(order, number);

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var statusBefore = order.Status;
            order.Status = OrderStatus.Pending;
            order.VippsReference = reference;
            order.UpdatedAt = DateTime.UtcNow;
            order.Payments.Add(new OrderPayment { VippsReference = reference });
            try
            {
                // Locks the order row until the commit, so a release of the build or a second click waits for this one.
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // A second click, a webhook or the admin changed the order after the read.
                await tx.RollbackAsync(CancellationToken.None);
                return PaymentInProgress();
            }

            if (!await HoldsBuildAsync(db, order, ct))
            {
                await tx.RollbackAsync(CancellationToken.None);
                return NotValid();
            }

            try
            {
                var payment = await vipps.CreatePaymentAsync(Checkout.PaymentRequest(order, config), $"payment-{reference}", ct);
                order.IsTest = payment.IsTest;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                log.LogInformation("Order {OrderId} started payment {VippsReference} from its payment link, it was {Status}", order.Id, reference, statusBefore);
                return TypedResults.Ok(new CheckoutResponse(payment.RedirectUrl, order.PublicToken));
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(CancellationToken.None);
                log.LogError(ex, "Vipps could not create payment {VippsReference} for order {OrderId}.", reference, order.Id);
                return ShopProblems.Code(StatusCodes.Status502BadGateway, "paymentProviderUnavailable",
                    "Vipps could not start the payment. Try again later.");
            }
        }

        private static IResult NotValid() =>
            ShopProblems.Code(StatusCodes.Status404NotFound, "paymentLinkInvalid", "The payment link is not valid.");

        private static IResult PaymentInProgress() =>
            ShopProblems.Code(StatusCodes.Status409Conflict, "paymentInProgress",
                "A payment for this order is already open in Vipps. Finish it or wait a few minutes and try again.");

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                app.MapPost("/api/admin/shop/orders/{id:int}/payment-link", Send).RequireAuthorization(Policies.Admin);

                app.MapGet("/api/shop/pay/{token}",
                        (string token, HttpContext http, ApplicationDbContext db, CancellationToken ct) =>
                        {
                            http.Response.Headers.CacheControl = "no-store";
                            return Get(token, db, ct);
                        })
                    .AllowAnonymous();
                app.MapPost("/api/shop/pay/{token}", Pay).AllowAnonymous();
            }
        }
    }

    /// <summary>The order on the pay page: its number, the build, the total in øre, when the link runs out and the order's locale.</summary>
    public record PayLinkDto(string Reference, string BuildTitle, int TotalInOre, DateTime ExpiresAt, string Locale);
}
