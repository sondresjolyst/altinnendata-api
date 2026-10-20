namespace altinnendata_api.Models.Shop
{
    /// <summary>
    /// One Vipps payment of an order. An order gets a new one each time the buyer starts paying from a payment link,
    /// and earlier ones are kept so a late event for one of them is known and ignored.
    /// </summary>
    public class OrderPayment
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public required string VippsReference { get; set; }

        /// <summary>The last Vipps event or state applied to this payment, such as CREATED, AUTHORIZED or EXPIRED.</summary>
        public string State { get; set; } = Created;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public const string Created = "CREATED";
    }
}
