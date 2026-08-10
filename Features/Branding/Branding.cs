using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Services;

namespace altinnendata_api.Features.Branding
{
    public record BrandingResponse(string? LogoData, string? LogoContentType, string? IconData, string? IconContentType);

    /// <summary>Get (public) / update (admin) the site logo and icon, stored as base64.</summary>
    public static class Branding
    {
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
                settings = new altinnendata_api.Models.Admin.AppSettings { Id = 1 };
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

        private static async Task<(string Data, string ContentType)> ReadImageAsync(IFormFile file, CancellationToken ct)
        {
            const long maxBytes = 2 * 1024 * 1024;
            if (file.Length == 0 || file.Length > maxBytes)
                throw new AppValidationException("Image must be between 1 byte and 2 MB.");
            if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                throw new AppValidationException("File must be an image.");

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, ct);
            return (Convert.ToBase64String(ms.ToArray()), file.ContentType);
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
