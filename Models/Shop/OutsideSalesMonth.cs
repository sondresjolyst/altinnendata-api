namespace altinnendata_api.Models.Shop
{
    /// <summary>
    /// What the business sold outside the shop in one month, entered by hand. It counts toward the
    /// VAT registration threshold together with the shop's own sales. Amounts are øre.
    /// </summary>
    public class OutsideSalesMonth
    {
        /// <summary>The first day of the month, in Norwegian time.</summary>
        public DateOnly Month { get; set; }

        public int AmountInOre { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
