using FluentValidation;
using Microsoft.EntityFrameworkCore;
using altinnendata_api.Models;
using altinnendata_api.Models.Admin;
using altinnendata_api.Models.Shop;
using SjolystInnovation.AppKit.SiteSettings;

namespace altinnendata_api.Features.Shop
{
    public record AdminOrderSummaryDto(
        int Id,
        string Reference,
        string Status,
        string BuyerName,
        string BuildTitle,
        int? PcBuildId,
        string DeliveryMethod,
        int TotalInOre,
        int CapturedInOre,
        int RefundedInOre,
        bool IsTest,
        DateTimeOffset? CaptureGuaranteedUntil,
        string? InvoiceNumber,
        DateTime CreatedAt,
        DateTime? ShippedAt,
        string? ShippingNote,
        DateTime? ConfirmationSentAt,
        DateTime TermsAcceptedAt,
        string? TermsVersion)
    {
        public static AdminOrderSummaryDto From(Order o) =>
            new(o.Id, o.Reference, o.Status.ToString(), o.BuyerName, o.BuildTitle, o.PcBuildId, o.DeliveryMethod.ToString(),
                o.TotalInOre, o.CapturedInOre, o.RefundedInOre, o.IsTest, o.CaptureGuaranteedUntil, o.InvoiceNumber, o.CreatedAt,
                o.ShippedAt, o.ShippingNote, o.ConfirmationSentAt, o.TermsAcceptedAt, o.TermsVersion);
    }

    public record AdminOrderDto(
        int Id,
        string Reference,
        string Status,
        string Locale,
        string BuyerName,
        string BuyerEmail,
        string BuyerPhone,
        string? StreetAddress,
        string? PostalCode,
        string? City,
        string DeliveryMethod,
        string DeliveryLabel,
        int DeliveryPriceInOre,
        bool WithinDistanceConfirmed,
        string? PickupNote,
        int? PcBuildId,
        string BuildTitle,
        string BuildSlug,
        int BuildPriceInOre,
        int TotalInOre,
        int CapturedInOre,
        int RefundedInOre,
        bool IsTest,
        DateTimeOffset? CaptureGuaranteedUntil,
        string? InvoiceNumber,
        DateTime TermsAcceptedAt,
        DateTime? AuthorizedAt,
        DateTime? CapturedAt,
        DateTime? CancelledAt,
        DateTime? RefundedAt,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        string? TermsVersion,
        string? TermsAcceptedIp,
        DateTime? ConfirmationSentAt,
        DateTime? ShippedAt,
        string? ShippingNote,
        IReadOnlyList<OrderRefundDto> Refunds,
        DateTime? DeliveryDate,
        string DeliveryPlace,
        DateTime? ReservationEndedAt,
        bool HoldsBuild,
        string? ReleaseBuildBlockedReason)
    {
        /// <summary>
        /// The order as the admin pages show it, with its refunds, oldest first, and where it is delivered. Every
        /// admin order endpoint answers with this.
        /// </summary>
        public static async Task<AdminOrderDto> LoadAsync(ApplicationDbContext db, Order o, CancellationToken ct)
        {
            var refunds = await db.OrderRefunds.AsNoTracking()
                .Where(r => r.OrderId == o.Id)
                .OrderBy(r => r.RefundedAt).ThenBy(r => r.Id)
                .Select(r => new OrderRefundDto(r.AmountInOre, r.RefundedAt, r.Source.ToString()))
                .ToListAsync(ct);
            var seller = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(a => a.Id == 1, ct) ?? new AppSettings();
            var holdsBuild = o.PcBuildId is int buildId
                && await db.PcBuilds.AnyAsync(b => b.Id == buildId && b.HeldByOrderId == o.Id && b.Availability == BuildAvailability.Reserved, ct);
            return From(o, refunds, seller, holdsBuild);
        }

        /// <summary>The buyer's address, or the seller's for pickup, as one line.</summary>
        public static string DeliveryPlaceOf(Order o, AppSettings seller) =>
            o.DeliveryMethod == Models.Shop.DeliveryMethod.Pickup
                ? seller.FormattedAddress
                : PostalAddress.Format(o.StreetAddress, o.PostalCode, o.City);

        /// <summary>
        /// Why "release the build" cannot be used now, as the code its endpoint would answer with: <c>wrongStatus</c>
        /// unless the order is failed or cancelled, <c>buildNotHeld</c> when it no longer holds the build. Null when it can.
        /// </summary>
        public static string? ReleaseBuildBlockedReasonOf(Order o, bool holdsBuild) =>
            o.Status is not (OrderStatus.Failed or OrderStatus.Cancelled) ? "wrongStatus"
            : !holdsBuild ? "buildNotHeld"
            : null;

        private static AdminOrderDto From(Order o, IReadOnlyList<OrderRefundDto> refunds, AppSettings seller, bool holdsBuild) =>
            new(o.Id, o.Reference, o.Status.ToString(), o.Locale, o.BuyerName, o.BuyerEmail, o.BuyerPhone,
                o.StreetAddress, o.PostalCode, o.City, o.DeliveryMethod.ToString(), o.DeliveryLabel, o.DeliveryPriceInOre,
                o.WithinDistanceConfirmed, o.PickupNote, o.PcBuildId, o.BuildTitle, o.BuildSlug, o.BuildPriceInOre,
                o.TotalInOre, o.CapturedInOre, o.RefundedInOre, o.IsTest, o.CaptureGuaranteedUntil, o.InvoiceNumber,
                o.TermsAcceptedAt, o.AuthorizedAt, o.CapturedAt, o.CancelledAt, o.RefundedAt, o.CreatedAt, o.UpdatedAt,
                o.TermsVersion, o.TermsAcceptedIp, o.ConfirmationSentAt, o.ShippedAt, o.ShippingNote,
                refunds, o.ShippedAt, DeliveryPlaceOf(o, seller),
                o.ReservationEndedAt, holdsBuild, ReleaseBuildBlockedReasonOf(o, holdsBuild));
    }

    /// <summary>One refund: the amount in øre, when, and whether the admin or a Vipps webhook recorded it.</summary>
    public record OrderRefundDto(int AmountInOre, DateTime RefundedAt, string Source);

    /// <summary>
    /// How much of a paid order to refund, in øre. Left out, it refunds everything not yet refunded.
    /// <paramref name="ExpectedRefundedInOre"/> is the refunded total the admin's page was loaded with, and is required.
    /// </summary>
    public record RefundRequest(int? AmountInOre, int? ExpectedRefundedInOre)
    {
        /// <summary>The field a refund amount outside 1 to what is left is reported on.</summary>
        public const string AmountField = "amountInOre";

        /// <summary>The field a missing refunded total is reported on.</summary>
        public const string ExpectedField = "expectedRefundedInOre";
    }

    /// <summary>The optional note to the buyer when the admin marks an order as sent or ready for pickup.</summary>
    public record ShippedRequest(string? Note)
    {
        public const int MaxNoteLength = 500;

        /// <summary>The field a note that is too long is reported on.</summary>
        public const string NoteField = "note";
    }

    /// <summary>The invoice number the admin made for the order. Surrounding spaces are dropped and an empty value clears it.</summary>
    public record InvoiceNumberRequest(string? InvoiceNumber);

    public class InvoiceNumberRequestValidator : AbstractValidator<InvoiceNumberRequest>
    {
        public const int MaxLength = 50;

        public InvoiceNumberRequestValidator()
        {
            RuleFor(x => x.InvoiceNumber)
                .Must(v => v == null || v.Trim().Length <= MaxLength)
                .WithMessage($"The invoice number must be {MaxLength} characters or fewer.");
        }
    }
}
