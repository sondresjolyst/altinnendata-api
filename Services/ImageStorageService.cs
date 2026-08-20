using SkiaSharp;

namespace altinnendata_api.Services
{
    public class ImageStorageService : FileStorageBase, IImageStorageService
    {
        private static readonly int[] TargetWidths = [384, 640, 768, 1024, 1366];
        private const int MaxWebpWidth = 2000;
        private const int WebpQuality = 80;
        private const int MaxSourceDimension = 8000;

        private static readonly Dictionary<string, string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = ".jpg",
            ["image/png"] = ".png",
            ["image/webp"] = ".webp",
            ["image/gif"] = ".gif",
            ["image/avif"] = ".avif",
        };

        private readonly ILogger<ImageStorageService> _logger;

        public ImageStorageService(IConfiguration configuration, ILogger<ImageStorageService> logger)
            : this(LoadOptions(configuration), logger) { }

        private ImageStorageService(StorageOptions options, ILogger<ImageStorageService> logger)
            : base(options.ImagesPath, options.MaxImageBytes)
        {
            _logger = logger;
        }

        private static StorageOptions LoadOptions(IConfiguration configuration) =>
            configuration.GetSection("Storage").Get<StorageOptions>() ?? new StorageOptions();

        public async Task<(string StoredPath, string ContentType, long SizeBytes)> SaveAsync(IFormFile file, CancellationToken ct = default)
        {
            if (file.Length == 0)
                throw new AppValidationException("Uploaded file is empty.");
            if (file.Length > MaxBytes)
                throw new AppValidationException($"Image exceeds the {MaxBytes / 1_048_576} MB limit.");
            if (!Extensions.TryGetValue(file.ContentType, out var extension))
                throw new AppValidationException("Unsupported image type.");

            var storedName = $"{Guid.NewGuid():N}{extension}";
            await WriteAsync(storedName, file, ct);

            _logger.LogInformation("Stored image {StoredName} ({Bytes} bytes)", storedName, file.Length);
            return (storedName, file.ContentType, file.Length);
        }

        public (int Width, int Height)? Probe(string storedPath)
        {
            try
            {
                using var input = OpenRead(storedPath);
                using var codec = SKCodec.Create(input);
                if (codec == null) return null;

                var info = codec.Info;
                if (info.Width == 0 || info.Height == 0) return null;

                // Decoding applies a quarter-turn origin, so the displayed dimensions are the
                // stored ones with the axes swapped.
                return IsQuarterTurn(codec.EncodedOrigin)
                    ? (info.Height, info.Width)
                    : (info.Width, info.Height);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read dimensions of {StoredPath}", storedPath);
                return null;
            }
        }

        private static bool IsQuarterTurn(SKEncodedOrigin origin) =>
            origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
                   or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;

        public async Task<IReadOnlyList<(int Width, string StoredPath, long SizeBytes)>> GenerateWebpVariantsAsync(string originalStoredPath, CancellationToken ct = default)
        {
            try
            {
                using var input = OpenRead(originalStoredPath);
                using var codec = SKCodec.Create(input);
                if (codec == null)
                {
                    _logger.LogWarning("Could not decode image {StoredPath} for webp generation", originalStoredPath);
                    return [];
                }

                var info = codec.Info;
                if (info.Width == 0 || info.Height == 0)
                    return [];
                if (info.Width > MaxSourceDimension || info.Height > MaxSourceDimension)
                {
                    _logger.LogWarning("Image {StoredPath} ({W}x{H}) exceeds max source dimension {Max}; skipping webp generation",
                        originalStoredPath, info.Width, info.Height, MaxSourceDimension);
                    return [];
                }

                // Decode downsampled (but not below the largest target width) to cap memory.
                var targetDecodeWidth = Math.Min(info.Width, MaxWebpWidth);
                var sample = 1;
                while (info.Width / (sample * 2) >= targetDecodeWidth) sample *= 2;
                var decodeInfo = new SKImageInfo(info.Width / sample, info.Height / sample);

                using var decoded = SKBitmap.Decode(codec, decodeInfo);
                if (decoded == null)
                {
                    _logger.LogWarning("Could not decode image {StoredPath} for webp generation", originalStoredPath);
                    return [];
                }

                // SKBitmap.Decode drops EXIF orientation; apply it manually.
                var original = ApplyOrientation(decoded, codec.EncodedOrigin);
                try
                {
                    return await EncodeWidthsAsync(original, ct);
                }
                finally
                {
                    if (!ReferenceEquals(original, decoded)) original.Dispose();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed generating webp variants for {StoredPath}", originalStoredPath);
                return [];
            }
        }

        private async Task<IReadOnlyList<(int Width, string StoredPath, long SizeBytes)>> EncodeWidthsAsync(SKBitmap original, CancellationToken ct)
        {
            var fullWidth = Math.Min(original.Width, MaxWebpWidth);
                var widths = TargetWidths
                    .Where(w => w < fullWidth)
                    .Append(fullWidth)
                    .Distinct()
                    .OrderBy(w => w)
                    .ToList();

                var results = new List<(int Width, string StoredPath, long SizeBytes)>();
                foreach (var width in widths)
                {
                    ct.ThrowIfCancellationRequested();

                    var height = (int)Math.Round((double)original.Height * width / original.Width);
                    var target = width == original.Width
                        ? original
                        : original.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
                    if (target == null) continue;

                    try
                    {
                        using var image = SKImage.FromBitmap(target);
                        using var data = image.Encode(SKEncodedImageFormat.Webp, WebpQuality);
                        if (data == null) continue;

                        var bytes = data.ToArray();
                        var storedName = $"{Guid.NewGuid():N}.webp";
                        await WriteBytesAsync(storedName, bytes, ct);
                        results.Add((width, storedName, bytes.Length));
                    }
                    finally
                    {
                        if (!ReferenceEquals(target, original)) target.Dispose();
                    }
                }

            _logger.LogInformation("Generated {Count} webp variants", results.Count);
            return results;
        }

        private static SKBitmap ApplyOrientation(SKBitmap bitmap, SKEncodedOrigin origin)
        {
            // Common rotations only; mirrored origins are rare and left as-is.
            switch (origin)
            {
                case SKEncodedOrigin.BottomRight: // 180°
                {
                    var rotated = new SKBitmap(bitmap.Width, bitmap.Height);
                    using var canvas = new SKCanvas(rotated);
                    canvas.RotateDegrees(180, bitmap.Width / 2f, bitmap.Height / 2f);
                    canvas.DrawBitmap(bitmap, 0, 0);
                    return rotated;
                }
                case SKEncodedOrigin.RightTop: // 90° CW
                {
                    var rotated = new SKBitmap(bitmap.Height, bitmap.Width);
                    using var canvas = new SKCanvas(rotated);
                    canvas.Translate(rotated.Width, 0);
                    canvas.RotateDegrees(90);
                    canvas.DrawBitmap(bitmap, 0, 0);
                    return rotated;
                }
                case SKEncodedOrigin.LeftBottom: // 270° CW
                {
                    var rotated = new SKBitmap(bitmap.Height, bitmap.Width);
                    using var canvas = new SKCanvas(rotated);
                    canvas.Translate(0, rotated.Height);
                    canvas.RotateDegrees(270);
                    canvas.DrawBitmap(bitmap, 0, 0);
                    return rotated;
                }
                default:
                    return bitmap;
            }
        }
    }
}
