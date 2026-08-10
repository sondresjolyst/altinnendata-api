namespace altinnendata_api.Services
{
    public interface IImageStorageService
    {
        Task<(string StoredPath, string ContentType, long SizeBytes)> SaveAsync(IFormFile file, CancellationToken ct = default);

        /// <summary>Generates resized webp renditions of an already-stored original. Returns an empty list if the source can't be decoded.</summary>
        Task<IReadOnlyList<(int Width, string StoredPath, long SizeBytes)>> GenerateWebpVariantsAsync(string originalStoredPath, CancellationToken ct = default);

        Stream OpenRead(string storedPath);
        void Delete(string storedPath);
        bool Exists(string storedPath);
    }
}
