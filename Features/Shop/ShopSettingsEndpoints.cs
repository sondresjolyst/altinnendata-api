using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SjolystInnovation.AppKit.SiteSettings;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;

namespace altinnendata_api.Features.Shop
{
    /// <summary>The admin's shop settings and the public shop configuration.</summary>
    public static class ShopSettingsEndpoints
    {
        /// <summary>The cache key the Vipps environment lookup shares, cleared when the mode changes.</summary>
        public const string VippsTestModeCacheKey = "shop_vipps_test_mode";

        /// <summary>The field error key for a buy button mode the other settings do not allow.</summary>
        public const string BuyButtonModeField = "buyButtonMode";

        public static async Task<IResult> GetAdmin(ApplicationDbContext db, CancellationToken ct)
        {
            var settings = await db.ShopSettings.AsNoTracking().FirstAsync(s => s.Id == 1, ct);
            var options = await LoadOptions(db, ct);
            var terms = await db.LegalPages.AsNoTracking().Where(p => p.Key == "terms").ToListAsync(ct);

            return TypedResults.Ok(ToDto(settings, options, terms));
        }

        public static async Task<IResult> Update(UpdateShopSettingsDto dto, ApplicationDbContext db, IMemoryCache cache, CancellationToken ct)
        {
            var settings = await db.ShopSettings.FirstAsync(s => s.Id == 1, ct);
            var options = await db.DeliveryOptions.ToListAsync(ct);
            var terms = await db.LegalPages.AsNoTracking().Where(p => p.Key == "terms").ToListAsync(ct);

            var mode = Enum.Parse<BuyButtonMode>(dto.BuyButton, true);
            if (mode == BuyButtonMode.Everyone && dto.VippsTestMode)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [BuyButtonModeField] = ["Turn off Vipps test mode before the buy button is shown to everyone."]
                });

            if (mode == BuyButtonMode.Everyone && !TermsUpdatedSince(terms, settings.CreatedAt))
                return ShopProblems.Code(StatusCodes.Status409Conflict, "termsNotUpdated",
                    "Save the terms in both languages before the buy button is shown to everyone.");

            var modeChanged = settings.VippsTestMode != dto.VippsTestMode;
            settings.BuyButton = mode;
            settings.VippsTestMode = dto.VippsTestMode;
            settings.UpdatedAt = DateTime.UtcNow;

            foreach (var input in dto.Delivery)
            {
                var option = options.First(o => o.Method == Enum.Parse<DeliveryMethod>(input.Method, true));
                option.Enabled = input.Enabled;
                option.PriceNok = input.PriceNok;
                option.LabelNo = input.LabelNo.Trim();
                option.LabelEn = input.LabelEn.Trim();
                option.DescriptionNo = Clean(input.DescriptionNo);
                option.DescriptionEn = Clean(input.DescriptionEn);
                option.MaxDistanceKm = input.MaxDistanceKm;
            }

            await db.SaveChangesAsync(ct);
            if (modeChanged) cache.Remove(VippsTestModeCacheKey);

            return TypedResults.Ok(ToDto(settings, options.OrderBy(o => o.Method).ToList(), terms));
        }

        public static async Task<IResult> GetPublic(ApplicationDbContext db, CancellationToken ct, string? locale = null)
        {
            var resolved = Locales.Normalize(locale);
            var settings = await db.ShopSettings.AsNoTracking().FirstAsync(s => s.Id == 1, ct);
            var options = (await LoadOptions(db, ct)).Where(o => o.Enabled);

            var delivery = options.Select(o => resolved == "en"
                ? new PublicDeliveryOptionDto(o.Method.ToString(), o.PriceNok, o.LabelEn, o.DescriptionEn, o.MaxDistanceKm)
                : new PublicDeliveryOptionDto(o.Method.ToString(), o.PriceNok, o.LabelNo, o.DescriptionNo, o.MaxDistanceKm))
                .ToList();

            return TypedResults.Ok(new ShopConfigDto(settings.BuyButton.ToString(), delivery));
        }

        /// <summary>True only when both the Norwegian and the English terms were saved after <paramref name="since"/>.</summary>
        public static bool TermsUpdatedSince(IEnumerable<LegalPage> terms, DateTime since)
        {
            var list = terms.ToList();
            return Locales.Supported.All(locale =>
                list.Any(p => p.Key == "terms" && p.Locale == locale && p.UpdatedAt > since));
        }

        private static async Task<List<DeliveryOption>> LoadOptions(ApplicationDbContext db, CancellationToken ct) =>
            (await db.DeliveryOptions.AsNoTracking().ToListAsync(ct)).OrderBy(o => o.Method).ToList();

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static ShopSettingsDto ToDto(ShopSettings settings, List<DeliveryOption> options, List<LegalPage> terms) =>
            new(
                settings.BuyButton.ToString(),
                settings.VippsTestMode,
                options.Select(o => new DeliveryOptionDto(o.Method.ToString(), o.Enabled, o.PriceNok, o.LabelNo, o.LabelEn, o.DescriptionNo, o.DescriptionEn, o.MaxDistanceKm)).ToList(),
                TermsUpdatedSince(terms, settings.CreatedAt),
                settings.CreatedAt);

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                app.MapGet("/api/shop/config", GetPublic).AllowAnonymous();

                var admin = app.MapGroup("/api/admin/shop/settings").RequireAuthorization(Policies.Admin);
                admin.MapGet("", GetAdmin);
                admin.MapPut("", Update).WithValidation<UpdateShopSettingsDto>();
            }
        }
    }
}
