using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SjolystInnovation.AppKit.Vipps;
using altinnendata_api.Constants;
using altinnendata_api.Features.Builds;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;
using altinnendata_api.Services;

namespace altinnendata_api.Features.Shop
{
    /// <summary>Starts a purchase: holds the build for a new order and reserves the payment in Vipps.</summary>
    public static class Checkout
    {
        /// <summary>How long a checkout waits for another checkout's hold on the same build before it answers 409.</summary>
        public const string LockTimeout = "3s";

        private const string SetLockTimeout = "SET LOCAL lock_timeout = '" + LockTimeout + "'";

        public static async Task<IResult> Handle(
            CheckoutRequest req,
            HttpContext http,
            ApplicationDbContext db,
            IVippsClient vipps,
            IConfiguration config,
            ILogger<CheckoutRequest> log,
            CancellationToken ct)
        {
            var settings = await db.ShopSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1, ct);
            var mode = settings?.BuyButton ?? BuyButtonMode.Off;
            if (mode == BuyButtonMode.Off || (mode == BuyButtonMode.AdminsOnly && !http.User.IsInRole(RoleNames.Admin)))
                return ShopProblems.Code(StatusCodes.Status404NotFound, "shopClosed", "The shop is closed.");

            var method = Enum.Parse<DeliveryMethod>(req.DeliveryMethod, true);
            var delivery = await db.DeliveryOptions.AsNoTracking().FirstOrDefaultAsync(o => o.Method == method, ct);
            if (delivery is not { Enabled: true })
                return ShopProblems.Code(StatusCodes.Status400BadRequest, "deliveryUnavailable", "This delivery method is not available.");

            var build = await db.PcBuilds.AsNoTracking()
                .Include(b => b.Translations)
                .FirstOrDefaultAsync(b => b.Slug == req.BuildSlug, ct);
            if (build is not { Published: true, Availability: BuildAvailability.Available, PriceNok: not null })
                return BuildUnavailable();

            var locale = Locales.Normalize(req.Locale);
            VippsPhoneNumber.TryNormalize(req.Phone, out var phone);
            var order = NewOrder(req, locale, phone, build, delivery);

            await using var tx = await db.Database.BeginTransactionAsync(ct);
            // A second checkout of the same build waits on this hold's row lock, so bound the wait.
            await db.Database.ExecuteSqlRawAsync(SetLockTimeout, ct);

            db.Orders.Add(order);
            await db.SaveChangesAsync(ct);
            order.Reference = NewReference(order.Id);

            bool held;
            try
            {
                held = await BuildHold.TryHoldAsync(db, build.Id, order.Id, 1, ct);
            }
            catch (Exception ex) when (IsLockTimeout(ex))
            {
                held = false;
            }

            if (!held)
            {
                await tx.RollbackAsync(CancellationToken.None);
                return BuildUnavailable();
            }

            // The Vipps call sits inside the transaction so a crash can never leave a hold behind
            // without a Vipps payment, whose webhook is the only thing that releases it.
            try
            {
                var payment = await vipps.CreatePaymentAsync(PaymentRequest(order, build.Id, config), order.Reference, ct);
                order.IsTest = payment.IsTest;
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return TypedResults.Ok(new CheckoutResponse(payment.RedirectUrl, order.PublicToken));
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync(CancellationToken.None);
                log.LogError(ex, "Vipps could not create the payment for {Reference}.", order.Reference);
                return ShopProblems.Code(StatusCodes.Status502BadGateway, "paymentProviderUnavailable",
                    "Vipps could not start the payment. Try again later.");
            }
        }

        /// <summary>The Vipps reference and idempotency key, <c>ae-000042-1a2b3c4d</c>.</summary>
        public static string NewReference(int orderId) =>
            $"ae-{orderId:D6}-{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}";

        /// <summary>The buyer's secret for the order status page, 32 lowercase hex characters.</summary>
        public static string NewToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

        private static IResult BuildUnavailable() =>
            ShopProblems.Code(StatusCodes.Status409Conflict, "buildUnavailable", "The computer is no longer available.");

        private static bool IsLockTimeout(Exception ex) =>
            ex is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable }
            || ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable };

        private static Order NewOrder(CheckoutRequest req, string locale, string phone, PcBuild build, DeliveryOption delivery)
        {
            var withAddress = delivery.Method is DeliveryMethod.Shipping or DeliveryMethod.HomeDelivery;
            var buildPrice = build.PriceNok!.Value * 100;
            var deliveryPrice = delivery.PriceNok * 100;

            return new Order
            {
                Reference = $"pending-{Guid.NewGuid():N}",
                PublicToken = NewToken(),
                Status = OrderStatus.Pending,
                Locale = locale,
                BuyerName = req.Name.Trim(),
                BuyerEmail = req.Email.Trim(),
                BuyerPhone = phone,
                StreetAddress = withAddress ? req.StreetAddress?.Trim() : null,
                PostalCode = withAddress ? req.PostalCode?.Trim() : null,
                City = withAddress ? req.City?.Trim() : null,
                DeliveryMethod = delivery.Method,
                DeliveryLabel = locale == "en" ? delivery.LabelEn : delivery.LabelNo,
                DeliveryPriceInOre = deliveryPrice,
                WithinDistanceConfirmed = delivery.Method == DeliveryMethod.HomeDelivery && req.WithinDistance,
                PickupNote = delivery.Method == DeliveryMethod.Pickup && !string.IsNullOrWhiteSpace(req.PickupNote) ? req.PickupNote.Trim() : null,
                PcBuildId = build.Id,
                BuildTitle = BuildMapping.PickTranslation(build, locale)?.Title ?? build.Slug,
                BuildSlug = build.Slug,
                BuildPriceInOre = buildPrice,
                TotalInOre = buildPrice + deliveryPrice,
                TermsAcceptedAt = DateTime.UtcNow
            };
        }

        private static VippsCreatePaymentRequest PaymentRequest(Order order, int buildId, IConfiguration config)
        {
            List<VippsReceiptLine> lines =
            [
                new() { Id = $"build-{buildId}", Name = order.BuildTitle, UnitPriceInOre = order.BuildPriceInOre, Quantity = 1 }
            ];
            if (order.DeliveryPriceInOre > 0)
                lines.Add(new() { Id = $"delivery-{order.DeliveryMethod}", Name = order.DeliveryLabel, UnitPriceInOre = order.DeliveryPriceInOre, Quantity = 1 });

            return new VippsCreatePaymentRequest
            {
                Reference = order.Reference,
                AmountInOre = order.TotalInOre,
                PhoneNumber = order.BuyerPhone,
                ReturnUrl = SiteLinks.ShopReturn(config, order.Locale, order.PublicToken),
                Description = $"Altinnendata {order.Reference}",
                ReceiptLines = lines
            };
        }

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                app.MapPost("/api/shop/checkout", Handle)
                    .AllowAnonymous()
                    .WithValidation<CheckoutRequest>();
            }
        }
    }
}
