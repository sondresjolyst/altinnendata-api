using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Services;
using SkiaSharp;

namespace altinnendata_api.Features.Branding
{
    public record BrandingResponse(string? LogoData, string? LogoContentType, string? IconData, string? IconContentType);

    /// <summary>Get (public) and update (admin) the site logo and icon, stored as base64.</summary>
    public static class Branding
    {
        public const long MaxImageBytes = 2 * 1024 * 1024;

        // The formats a logo or icon may have. The content type comes from the decoded image, never from
        // what the browser claims. SVG is not accepted, since it can carry script.
        private static readonly Dictionary<SKEncodedImageFormat, string> ContentTypes = new()
        {
            [SKEncodedImageFormat.Png] = "image/png",
            [SKEncodedImageFormat.Jpeg] = "image/jpeg",
            [SKEncodedImageFormat.Webp] = "image/webp",
            [SKEncodedImageFormat.Gif] = "image/gif",
            [SKEncodedImageFormat.Ico] = "image/x-icon",
        };

        public static async Task<IResult> Get(ApplicationDbContext db, CancellationToken ct)
        {
            var settings = await db.AppSettings.FindAsync([1], ct);
            return TypedResults.Ok(new BrandingResponse(
                settings?.LogoData, settings?.LogoContentType, settings?.IconData, settings?.IconContentType));
        }

        public static async Task<IResult> Update(HttpContext http, ApplicationDbContext db, CancellationToken ct)
        {
            var form = await http.Request.ReadFormAsync(ct);
            var settings = await db.AppSettings.FindAsync([1], ct);
            if (settings == null)
            {
                settings = new Models.Admin.AppSettings { Id = 1 };
                db.AppSettings.Add(settings);
            }

            if (form["removeLogo"] == "true")
                (settings.LogoData, settings.LogoContentType) = (null, null);
            else if (form.Files["logo"] is { } logo)
                (settings.LogoData, settings.LogoContentType) = await ReadImageAsync(logo, ct);

            if (form["removeIcon"] == "true")
                (settings.IconData, settings.IconContentType) = (null, null);
            else if (form.Files["icon"] is { } icon)
                (settings.IconData, settings.IconContentType) = await ReadImageAsync(icon, ct);

            await db.SaveChangesAsync(ct);
            return await Get(db, ct);
        }

        internal static async Task<(string Data, string ContentType)> ReadImageAsync(IFormFile file, CancellationToken ct)
        {
            if (file.Length == 0 || file.Length > MaxImageBytes)
                throw new AppValidationException("Image must be between 1 byte and 2 MB.");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            var bytes = ms.ToArray();

            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec == null || !ContentTypes.TryGetValue(codec.EncodedFormat, out var contentType))
                throw new AppValidationException("The file must be a PNG, JPEG, WebP, GIF or ICO image.");

            return (Convert.ToBase64String(bytes), contentType);
        }

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                app.MapGet("/api/branding", Get).AllowAnonymous();
                app.MapPut("/api/branding", Update).RequireAuthorization(Policies.Admin).DisableAntiforgery();
            }
        }
    }
}
