namespace altinnendata_api.Models.Shop
{
    public enum BuyButtonMode { Off, AdminsOnly, Everyone }

    /// <summary>The single settings row of the shop. <see cref="CreatedAt"/> is the baseline the terms pages are compared against.</summary>
    public class ShopSettings
    {
        public int Id { get; set; } = 1;
        public BuyButtonMode BuyButton { get; set; } = BuyButtonMode.Off;
        public bool VippsTestMode { get; set; } = true;

        /// <summary>The Meta Pixel ID, 5 to 20 digits. Null while the Pixel is off.</summary>
        public string? MetaPixelId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
