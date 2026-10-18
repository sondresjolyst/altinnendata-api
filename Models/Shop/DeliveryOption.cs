namespace altinnendata_api.Models.Shop
{
    public enum DeliveryMethod { Shipping, HomeDelivery, Pickup }

    /// <summary>One row per <see cref="DeliveryMethod"/>. The price is whole kroner.</summary>
    public class DeliveryOption
    {
        public int Id { get; set; }
        public DeliveryMethod Method { get; set; }
        public bool Enabled { get; set; }
        public int PriceNok { get; set; }
        public required string LabelNo { get; set; }
        public required string LabelEn { get; set; }
        public string? DescriptionNo { get; set; }
        public string? DescriptionEn { get; set; }
        public int? MaxDistanceKm { get; set; }
    }
}
