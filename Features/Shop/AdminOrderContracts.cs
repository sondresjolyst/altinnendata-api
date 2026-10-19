using FluentValidation;
using altinnendata_api.Models.Shop;

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
        DateTime CreatedAt)
    {
        public static AdminOrderSummaryDto From(Order o) =>
            new(o.Id, o.Reference, o.Status.ToString(), o.BuyerName, o.BuildTitle, o.PcBuildId, o.DeliveryMethod.ToString(),
                o.TotalInOre, o.CapturedInOre, o.RefundedInOre, o.IsTest, o.CaptureGuaranteedUntil, o.InvoiceNumber, o.CreatedAt);
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
        DateTime UpdatedAt)
    {
        public static AdminOrderDto From(Order o) =>
            new(o.Id, o.Reference, o.Status.ToString(), o.Locale, o.BuyerName, o.BuyerEmail, o.BuyerPhone,
                o.StreetAddress, o.PostalCode, o.City, o.DeliveryMethod.ToString(), o.DeliveryLabel, o.DeliveryPriceInOre,
                o.WithinDistanceConfirmed, o.PickupNote, o.PcBuildId, o.BuildTitle, o.BuildSlug, o.BuildPriceInOre,
                o.TotalInOre, o.CapturedInOre, o.RefundedInOre, o.IsTest, o.CaptureGuaranteedUntil, o.InvoiceNumber,
                o.TermsAcceptedAt, o.AuthorizedAt, o.CapturedAt, o.CancelledAt, o.RefundedAt, o.CreatedAt, o.UpdatedAt);
    }

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
