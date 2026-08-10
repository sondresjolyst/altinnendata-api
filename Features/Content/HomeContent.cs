using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Content;

namespace altinnendata_api.Features.Content
{
    /// <summary>Get (public) / replace (admin) the home page sections for one locale, stored as a JSON array blob.</summary>
    public static class HomeContent
    {
        private const string Empty = "[]";

        public static async Task<IResult> Get(ApplicationDbContext db, CancellationToken ct, string? locale = null)
        {
            var resolved = Locales.Normalize(locale);
            var content = await db.HomePageContents.AsNoTracking().FirstOrDefaultAsync(c => c.Locale == resolved, ct);

            // An unfilled language falls back to the default one rather than an empty page.
            content ??= resolved == Locales.Default
                ? null
                : await db.HomePageContents.AsNoTracking().FirstOrDefaultAsync(c => c.Locale == Locales.Default, ct);

            var json = string.IsNullOrWhiteSpace(content?.SectionsJson) ? Empty : content!.SectionsJson;
            return TypedResults.Content(json, "application/json");
        }

        public static async Task<IResult> Put(JsonElement sections, ApplicationDbContext db, CancellationToken ct, string? locale = null)
        {
            if (sections.ValueKind != JsonValueKind.Array)
                return TypedResults.Problem("Expected a JSON array of sections.", statusCode: StatusCodes.Status400BadRequest);

            if (locale != null && !Locales.IsSupported(locale))
                return TypedResults.Problem("Unsupported locale.", statusCode: StatusCodes.Status400BadRequest);

            var resolved = Locales.Normalize(locale);
            var json = sections.GetRawText();

            var content = await db.HomePageContents.FirstOrDefaultAsync(c => c.Locale == resolved, ct);
            if (content == null)
            {
                content = new HomePageContent { Locale = resolved, SectionsJson = json };
                db.HomePageContents.Add(content);
            }
            else
            {
                content.SectionsJson = json;
                content.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);
            return TypedResults.Content(json, "application/json");
        }

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                app.MapGet("/api/content/home", Get).AllowAnonymous();
                app.MapPut("/api/content/home", Put).RequireAuthorization(Policies.Admin);
            }
        }
    }
}
