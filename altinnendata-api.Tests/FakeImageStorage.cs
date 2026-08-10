using System.Text;
using Microsoft.AspNetCore.Http;
using altinnendata_api.Services;

namespace altinnendata_api.Tests;

public class FakeImageStorage : IImageStorageService
{
    public readonly Dictionary<string, byte[]> Files = new();
    public int SaveCount { get; private set; }
    public int DeleteCount { get; private set; }
    public int VariantCount { get; private set; }

    public async Task<(string StoredPath, string ContentType, long SizeBytes)> SaveAsync(IFormFile file, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();
        var name = $"{Guid.NewGuid():N}.png";
        Files[name] = bytes;
        SaveCount++;
        return (name, file.ContentType, bytes.Length);
    }

    public Task<IReadOnlyList<(int Width, string StoredPath, long SizeBytes)>> GenerateWebpVariantsAsync(string originalStoredPath, CancellationToken ct = default)
    {
        var name = $"{Guid.NewGuid():N}.webp";
        Files[name] = [9, 9, 9];
        VariantCount++;
        IReadOnlyList<(int Width, string StoredPath, long SizeBytes)> variants = [(800, name, 3)];
        return Task.FromResult(variants);
    }

    public Stream OpenRead(string storedPath) => new MemoryStream(Files[storedPath]);

    public void Delete(string storedPath)
    {
        Files.Remove(storedPath);
        DeleteCount++;
    }

    public bool Exists(string storedPath) => Files.ContainsKey(storedPath);

    public static IFormFile MakeImage(string name = "photo.png", string contentType = "image/png")
    {
        var bytes = Encoding.ASCII.GetBytes("fake-png-bytes");
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "file", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType
        };
    }
}
