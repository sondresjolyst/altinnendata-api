using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Primitives;
using altinnendata_api.Features.Branding;
using altinnendata_api.Models.Admin;
using altinnendata_api.Services;
using SkiaSharp;
using Xunit;

namespace altinnendata_api.Tests;

public class BrandingSliceTests : TestBase
{
    private static byte[] Image(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(4, 4);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Orange);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private static FormFile File(string name, byte[] bytes, string contentType) =>
        new(new MemoryStream(bytes), 0, bytes.Length, name, name + ".bin") { Headers = new HeaderDictionary(), ContentType = contentType };

    private static HttpContext Form(Dictionary<string, StringValues>? fields = null, params IFormFile[] files)
    {
        var http = new DefaultHttpContext();
        var collection = new FormFileCollection();
        collection.AddRange(files);
        http.Request.ContentType = "multipart/form-data; boundary=x";
        http.Request.Form = new FormCollection(fields ?? [], collection);
        return http;
    }

    [Fact]
    public async Task Get_IsEmptyWithoutBranding()
    {
        await using var db = CreateDbContext();
        var ok = Assert.IsType<Ok<BrandingResponse>>(await Branding.Get(db, default));
        Assert.Equal(new BrandingResponse(null, null, null, null), ok.Value);
    }

    [Fact]
    public async Task Update_StoresTheLogoAndIcon_WithTheTypeOfTheDecodedImage()
    {
        await using var db = CreateDbContext();
        db.AppSettings.Add(new AppSettings());
        await db.SaveChangesAsync();
        var png = Image(SKEncodedImageFormat.Png);
        var jpeg = Image(SKEncodedImageFormat.Jpeg);

        // The browser claims the wrong types. What is stored follows the bytes.
        var ok = Assert.IsType<Ok<BrandingResponse>>(await Branding.Update(
            Form(null, File("logo", png, "image/jpeg"), File("icon", jpeg, "image/png")), db, default));

        Assert.Equal((Convert.ToBase64String(png), "image/png"), (ok.Value!.LogoData, ok.Value.LogoContentType));
        Assert.Equal((Convert.ToBase64String(jpeg), "image/jpeg"), (ok.Value.IconData, ok.Value.IconContentType));
        var stored = await db.AppSettings.FindAsync(1);
        Assert.Equal("image/png", stored!.LogoContentType);
    }

    [Fact]
    public async Task Update_CreatesTheSettingsRowWhenMissing()
    {
        await using var db = CreateDbContext();
        await Branding.Update(Form(null, File("logo", Image(SKEncodedImageFormat.Webp), "image/webp")), db, default);
        Assert.Equal("image/webp", (await db.AppSettings.FindAsync(1))!.LogoContentType);
    }

    [Fact]
    public async Task Update_RemovesALogoAndKeepsTheIcon()
    {
        await using var db = CreateDbContext();
        db.AppSettings.Add(new AppSettings { LogoData = "bG9nbw==", LogoContentType = "image/png", IconData = "aWNvbg==", IconContentType = "image/png" });
        await db.SaveChangesAsync();

        var ok = Assert.IsType<Ok<BrandingResponse>>(await Branding.Update(
            Form(new() { ["removeLogo"] = "true" }), db, default));

        Assert.Null(ok.Value!.LogoData);
        Assert.Null(ok.Value.LogoContentType);
        Assert.Equal("aWNvbg==", ok.Value.IconData);
    }

    public static TheoryData<string, byte[], string> NotImages => new()
    {
        { "svg", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"), "image/svg+xml" },
        { "html claiming png", Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"), "image/png" },
        { "truncated png", Image(SKEncodedImageFormat.Png)[..8], "image/png" },
        { "empty", [], "image/png" },
    };

    [Theory]
    [MemberData(nameof(NotImages))]
    public async Task Update_RefusesWhatIsNotAnAcceptedImage(string _, byte[] bytes, string claimed)
    {
        await using var db = CreateDbContext();
        db.AppSettings.Add(new AppSettings());
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<AppValidationException>(() => Branding.Update(Form(null, File("logo", bytes, claimed)), db, default));
        Assert.Null((await db.AppSettings.FindAsync(1))!.LogoData);
    }

    [Fact]
    public async Task Update_RefusesAnImageOver2Mb()
    {
        await using var db = CreateDbContext();
        var big = new byte[Branding.MaxImageBytes + 1];
        Image(SKEncodedImageFormat.Png).CopyTo(big, 0);

        await Assert.ThrowsAsync<AppValidationException>(() => Branding.Update(Form(null, File("logo", big, "image/png")), db, default));
    }
}
