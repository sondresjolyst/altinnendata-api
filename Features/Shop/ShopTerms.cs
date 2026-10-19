using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SjolystInnovation.AppKit.SiteSettings;
using altinnendata_api.Constants;
using altinnendata_api.Models;

namespace altinnendata_api.Features.Shop
{
    /// <summary>The terms page a buyer accepts at checkout, and its version stamp.</summary>
    public static class ShopTerms
    {
        public const string Key = "terms";

        /// <summary>The version the shop config reports while no terms page exists. Checkout then accepts any version.</summary>
        public const string NoTermsVersion = "none";

        /// <summary>The longest version a checkout may send.</summary>
        public const int MaxVersionLength = 40;

        /// <summary>The terms page in <paramref name="locale"/>, or the Norwegian page when that locale has none.</summary>
        public static async Task<LegalPage?> FindAsync(ApplicationDbContext db, string locale, CancellationToken ct)
        {
            var pages = await db.LegalPages.AsNoTracking()
                .Where(p => p.Key == Key && (p.Locale == locale || p.Locale == Locales.Default))
                .ToListAsync(ct);
            return pages.FirstOrDefault(p => p.Locale == locale) ?? pages.FirstOrDefault(p => p.Locale == Locales.Default);
        }

        /// <summary>
        /// The page's version stamp: when it was last saved, in UTC to the microsecond Postgres keeps,
        /// for example <c>2026-10-19T12:34:56.123456Z</c>.
        /// </summary>
        public static string VersionOf(LegalPage? page) =>
            page == null
                ? NoTermsVersion
                : DateTime.SpecifyKind(page.UpdatedAt, DateTimeKind.Utc)
                    .ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
    }
}
