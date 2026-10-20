namespace altinnendata_api.Models.Shop
{
    /// <summary>
    /// Who recorded a refund: the admin's refund action, a REFUNDED webhook, the payment polling that found a refund
    /// whose webhook was lost, or the migration that made rows for refunds recorded before.
    /// </summary>
    public enum RefundSource { Admin, Webhook, Migrated, Polling }

    /// <summary>
    /// One refund on an order, so the accounts can have a credit note per refund. The order keeps the sum in
    /// <see cref="Order.RefundedInOre"/>. Amounts are øre.
    /// </summary>
    public class OrderRefund
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public int AmountInOre { get; set; }
        public DateTime RefundedAt { get; set; }

        /// <summary>The idempotency key of the refund call, or of the webhook event that recorded it.</summary>
        public string? IdempotencyKey { get; set; }

        public RefundSource Source { get; set; }
    }
}
