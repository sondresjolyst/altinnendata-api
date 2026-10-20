namespace altinnendata_api.Models.Shop
{
    public enum BuyButtonMode { Off, AdminsOnly, Everyone }

    /// <summary>The single settings row of the shop. <see cref="CreatedAt"/> is the baseline the terms pages are compared against.</summary>
    public class ShopSettings
    {
        public int Id { get; set; } = 1;
        public BuyButtonMode BuyButton { get; set; } = BuyButtonMode.Off;
        public bool VippsTestMode { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
