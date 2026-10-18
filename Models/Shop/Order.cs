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

        public bool IsTest { get; set; }
        public DateTimeOffset? CaptureGuaranteedUntil { get; set; }
        public string? InvoiceNumber { get; set; }

        public DateTime TermsAcceptedAt { get; set; }
        public DateTime? AuthorizedAt { get; set; }
        public DateTime? CapturedAt { get; set; }
        public DateTime? CancelledAt { get; set; }
        public DateTime? RefundedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Postgres xmin. A save from a stale read fails, so a webhook and an admin action cannot overwrite each other.</summary>
        public uint Version { get; set; }
    }
}
