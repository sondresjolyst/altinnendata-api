namespace altinnendata_api.Models.Shop
{
    public enum OrderStatus { Pending, Reserved, Paid, Failed, Cancelled, Refunded }

    /// <summary>
    /// One purchase of one build. The buyer, delivery and build details are copied in at checkout,
    /// so the order still reads correctly after the build is edited or deleted. Amounts are øre.
    /// </summary>
    public class Order
    {
        public int Id { get; set; }
        public required string Reference { get; set; }

        /// <summary>
        /// The reference of the order's current Vipps payment. The first payment uses <see cref="Reference"/>. A payment
        /// started from a payment link uses <c>{Reference}-{n}</c>. Every payment of the order is in <see cref="Payments"/>.
        /// </summary>
        public required string VippsReference { get; set; }

        /// <summary>One row per Vipps payment of the order, the current one included. Deleted with the order.</summary>
        public ICollection<OrderPayment> Payments { get; set; } = [];
        public required string PublicToken { get; set; }
        public OrderStatus Status { get; set; } = OrderStatus.Pending;
        public required string Locale { get; set; }

        public required string BuyerName { get; set; }
        public required string BuyerEmail { get; set; }

        /// <summary>The normalised Vipps phone number.</summary>
        public required string BuyerPhone { get; set; }

        public string? StreetAddress { get; set; }
        public string? PostalCode { get; set; }
        public string? City { get; set; }

        public DeliveryMethod DeliveryMethod { get; set; }

        /// <summary>The delivery label in the buyer's locale.</summary>
        public required string DeliveryLabel { get; set; }

        public int DeliveryPriceInOre { get; set; }
        public bool WithinDistanceConfirmed { get; set; }
        public string? PickupNote { get; set; }

        public int? PcBuildId { get; set; }
        public PcBuild? PcBuild { get; set; }
        public required string BuildTitle { get; set; }
        public required string BuildSlug { get; set; }
        public int BuildPriceInOre { get; set; }

        public int TotalInOre { get; set; }
        public int CapturedInOre { get; set; }
        public int RefundedInOre { get; set; }

        /// <summary>
        /// The idempotency key and amount of the last admin refund sent to Vipps that Vipps has not shown yet.
        /// Cleared once the refunded total moves past it.
        /// </summary>
        public string? PendingRefundKey { get; set; }
        public int? PendingRefundInOre { get; set; }

        /// <summary>One row per refund. Deleted with the order.</summary>
        public ICollection<OrderRefund> Refunds { get; set; } = [];

        public bool IsTest { get; set; }
        public DateTimeOffset? CaptureGuaranteedUntil { get; set; }
        public string? InvoiceNumber { get; set; }

        public DateTime TermsAcceptedAt { get; set; }

        /// <summary>The version stamp of the terms the buyer accepted, as the checkout sent it.</summary>
        public string? TermsVersion { get; set; }

        /// <summary>The buyer's IP address at checkout, cut to /24 for IPv4 and /48 for IPv6.</summary>
        public string? TermsAcceptedIp { get; set; }

        /// <summary>The terms page as it read at checkout, in <see cref="TermsLocale"/>. Empty when there was no terms page.</summary>
        public string? TermsTitle { get; set; }
        public string? TermsMarkdown { get; set; }
        public string? TermsLocale { get; set; }

        /// <summary>When the buyer's order confirmation with its PDF attachments was accepted for sending.</summary>
        public DateTime? ConfirmationSentAt { get; set; }

        /// <summary>How many times the confirmation was tried, and when last, so a retry can back off.</summary>
        public int ConfirmationAttempts { get; set; }
        public DateTime? ConfirmationAttemptedAt { get; set; }

        /// <summary>When the admin marked the order as sent or ready for pickup, with the note for the buyer.</summary>
        public DateTime? ShippedAt { get; set; }
        public string? ShippingNote { get; set; }
        public DateTime? AuthorizedAt { get; set; }

        /// <summary>
        /// When Vipps ended the reservation of a Reserved order without the admin asking for it (expired, aborted,
        /// terminated or cancelled). The build stays held until the admin releases it.
        /// </summary>
        public DateTime? ReservationEndedAt { get; set; }

        /// <summary>
        /// When the admin asked Vipps to cancel the order. Set before Vipps is called, so the CANCELLED event that
        /// follows is known as the admin's own and releases the build as before. It stays set when the Vipps call
        /// fails, since Vipps may still have cancelled.
        /// </summary>
        public DateTime? AdminCancelRequestedAt { get; set; }

        /// <summary>
        /// The SHA-256 hash, lowercase hex, of the secret in the payment link the admin sent after the reservation ended.
        /// Null when no link is open: none was sent, it was used, the owner was told it ran out, or the build was released.
        /// The link itself is never stored.
        /// </summary>
        public string? PaymentLinkTokenHash { get; set; }
        public DateTime? PaymentLinkSentAt { get; set; }
        public DateTime? PaymentLinkExpiresAt { get; set; }

        /// <summary>When a payment started from the link was authorized. The link cannot be used after that.</summary>
        public DateTime? PaymentLinkUsedAt { get; set; }
        public DateTime? CapturedAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public DateTime? RefundedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Postgres xmin. A save from a stale read fails, so a webhook and an admin action cannot overwrite each other.</summary>
        public uint Version { get; set; }
    }
}
