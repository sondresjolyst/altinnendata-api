namespace altinnendata_api.Services
{
    public class StorageOptions
    {
        public string ImagesPath { get; set; } = "/data/images";
        public long MaxImageBytes { get; set; } = 5_242_880; // 5 MB
    }
}
