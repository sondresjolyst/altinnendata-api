using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using altinnendata_api.Services;
using Xunit;

namespace altinnendata_api.Tests;

/// <summary>
/// Exercises the real decoder. These dimensions become the width and height a page reserves for
/// an image, so an unapplied quarter-turn orientation would size every rotated photo wrongly.
/// </summary>
public class ImageProbeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"probe-{Guid.NewGuid():N}");
    private readonly ImageStorageService _storage;

    public ImageProbeTests()
    {
        Directory.CreateDirectory(_root);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:ImagesPath"] = _root,
                ["Storage:MaxImageBytes"] = "5242880",
            })
            .Build();
        _storage = new ImageStorageService(configuration, NullLogger<ImageStorageService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private string WriteImage(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.CornflowerBlue);

        var name = $"{Guid.NewGuid():N}{(format == SKEncodedImageFormat.Png ? ".png" : ".jpg")}";
        using var stream = File.Create(Path.Combine(_root, name));
        using var image = SKImage.FromBitmap(bitmap);
        image.Encode(format, 90).SaveTo(stream);
        return name;
    }

    [Fact]
    public void Probe_ReportsTheStoredDimensions()
    {
        var stored = WriteImage(1600, 900);

        Assert.Equal((1600, 900), _storage.Probe(stored));
    }

    [Fact]
    public void Probe_HandlesAPortraitImage()
    {
        var stored = WriteImage(900, 1600);

        Assert.Equal((900, 1600), _storage.Probe(stored));
    }

    [Fact]
    public void Probe_ReadsAJpegAsWellAsAPng()
    {
        var stored = WriteImage(640, 480, SKEncodedImageFormat.Jpeg);

        Assert.Equal((640, 480), _storage.Probe(stored));
    }

    [Fact]
    public void Probe_ReturnsNothingForAFileThatIsNotAnImage()
    {
        var name = $"{Guid.NewGuid():N}.png";
        File.WriteAllText(Path.Combine(_root, name), "not an image");

        Assert.Null(_storage.Probe(name));
    }

    [Fact]
    public void Probe_ReturnsNothingForAMissingFile()
    {
        Assert.Null(_storage.Probe("does-not-exist.png"));
    }

    [Theory]
    [InlineData(SKEncodedOrigin.TopLeft, 1600, 900)]
    [InlineData(SKEncodedOrigin.BottomRight, 1600, 900)]
    [InlineData(SKEncodedOrigin.RightTop, 900, 1600)]
    [InlineData(SKEncodedOrigin.LeftBottom, 900, 1600)]
    public void Probe_SwapsTheAxesForAQuarterTurnOrientation(SKEncodedOrigin origin, int expectedWidth, int expectedHeight)
    {
        var stored = WriteJpegWithOrigin(1600, 900, origin);

        Assert.Equal((expectedWidth, expectedHeight), _storage.Probe(stored));
    }

    /// <summary>
    /// Writes a JPEG carrying an EXIF orientation tag. Skia encodes no EXIF of its own, so the
    /// segment is spliced in ahead of the image data.
    /// </summary>
    private string WriteJpegWithOrigin(int width, int height, SKEncodedOrigin origin)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap);
        var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 90).ToArray();

        var name = $"{Guid.NewGuid():N}.jpg";
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, origin == SKEncodedOrigin.TopLeft ? jpeg : InsertExifOrientation(jpeg, origin));
        return name;
    }

    private static byte[] InsertExifOrientation(byte[] jpeg, SKEncodedOrigin origin)
    {
        // Little-endian TIFF header with a single IFD entry: tag 0x0112 (orientation), type SHORT.
        var tiff = new List<byte>();
        tiff.AddRange("II"u8.ToArray());
        tiff.AddRange(BitConverter.GetBytes((ushort)42));
        tiff.AddRange(BitConverter.GetBytes((uint)8));       // offset of the first IFD
        tiff.AddRange(BitConverter.GetBytes((ushort)1));     // one entry
        tiff.AddRange(BitConverter.GetBytes((ushort)0x0112));
        tiff.AddRange(BitConverter.GetBytes((ushort)3));     // SHORT
        tiff.AddRange(BitConverter.GetBytes((uint)1));       // one value
        tiff.AddRange(BitConverter.GetBytes((ushort)origin));
        tiff.AddRange(BitConverter.GetBytes((ushort)0));     // padding to four bytes
        tiff.AddRange(BitConverter.GetBytes((uint)0));       // no next IFD

        var payload = new List<byte>("Exif"u8.ToArray()) { 0, 0 };
        payload.AddRange(tiff);

        var segment = new List<byte> { 0xFF, 0xE1 };
        var length = payload.Count + 2;
        segment.Add((byte)(length >> 8));
        segment.Add((byte)(length & 0xFF));
        segment.AddRange(payload);

        // After the two-byte SOI marker, before everything Skia wrote.
        var withExif = new List<byte>(jpeg[..2]);
        withExif.AddRange(segment);
        withExif.AddRange(jpeg[2..]);
        return withExif.ToArray();
    }
}
