using FluentValidation;
using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Content;

namespace altinnendata_api.Features.Content
{
    public record LegalPageBody(string Title, string BodyMarkdown);

    public record LegalPageDto(string Key, string Locale, string Title, string BodyMarkdown, DateTime UpdatedAt);

    public class LegalPageValidator : AbstractValidator<LegalPageBody>
    {
        public LegalPageValidator()
        {
            RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
            RuleFor(x => x.BodyMarkdown).NotEmpty();
        }
    }

    /// <summary>Terms, privacy and cookie text: public read per locale, admin write.</summary>
    public static class LegalPages
    {
        public static async Task<IResult> Get(string key, ApplicationDbContext db, CancellationToken ct, string? locale = null)
        {
            if (!LegalPageKeys.IsValid(key))
                return TypedResults.NotFound();

            var resolved = Locales.Normalize(locale);
            var page = await db.LegalPages.AsNoTracking().FirstOrDefaultAsync(p => p.Key == key && p.Locale == resolved, ct);

            page ??= resolved == Locales.Default
                ? null
                : await db.LegalPages.AsNoTracking().FirstOrDefaultAsync(p => p.Key == key && p.Locale == Locales.Default, ct);

            if (page == null) return TypedResults.NotFound();
            return TypedResults.Ok(new LegalPageDto(page.Key, page.Locale, page.Title, page.BodyMarkdown, page.UpdatedAt));
        }

        public static async Task<IResult> GetAll(ApplicationDbContext db, CancellationToken ct)
        {
            var pages = await db.LegalPages
                .AsNoTracking()
                .OrderBy(p => p.Key)
                .ThenBy(p => p.Locale)
                .Select(p => new LegalPageDto(p.Key, p.Locale, p.Title, p.BodyMarkdown, p.UpdatedAt))
                .ToListAsync(ct);
            return TypedResults.Ok(pages);
        }

        public static async Task<IResult> Put(string key, string locale, LegalPageBody body, ApplicationDbContext db, CancellationToken ct)
        {
            if (!LegalPageKeys.IsValid(key))
                return TypedResults.Problem("Unknown legal page.", statusCode: StatusCodes.Status400BadRequest);
            if (!Locales.IsSupported(locale))
                return TypedResults.Problem("Unsupported locale.", statusCode: StatusCodes.Status400BadRequest);

            var resolved = Locales.Normalize(locale);
            var page = await db.LegalPages.FirstOrDefaultAsync(p => p.Key == key && p.Locale == resolved, ct);
            if (page == null)
            {
                page = new LegalPage { Key = key, Locale = resolved, Title = body.Title, BodyMarkdown = body.BodyMarkdown };
                db.LegalPages.Add(page);
            }
            else
            {
                page.Title = body.Title;
                page.BodyMarkdown = body.BodyMarkdown;
                page.UpdatedAt = DateTime.UtcNow;
            }

            await db.SaveChangesAsync(ct);
            return TypedResults.Ok(new LegalPageDto(page.Key, page.Locale, page.Title, page.BodyMarkdown, page.UpdatedAt));
        }

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                app.MapGet("/api/content/legal/{key}", Get).AllowAnonymous();
                app.MapGet("/api/content/legal", GetAll).RequireAuthorization(Policies.Admin);
                app.MapPut("/api/content/legal/{key}/{locale}", Put)
                    .RequireAuthorization(Policies.Admin)
                    .WithValidation<LegalPageBody>();
            }
        }
    }
}
