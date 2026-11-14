using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;

namespace altinnendata_api.Features.Marketing
{
    /// <summary>The admin's marketing settings and the public marketing configuration.</summary>
    public static class MarketingSettingsEndpoints
    {
        public static async Task<IResult> GetAdmin(ApplicationDbContext db, CancellationToken ct)
        {
            var settings = await db.ShopSettings.AsNoTracking().FirstAsync(s => s.Id == 1, ct);
            return TypedResults.Ok(AdminView(settings));
        }

        private static MarketingSettingsDto AdminView(ShopSettings settings) =>
            new(settings.MetaPixelId, settings.TikTokPixelId, settings.MetaCapiToken != null, settings.TikTokEventsToken != null);

        public static async Task<IResult> Update(UpdateMarketingSettingsDto dto, ApplicationDbContext db, CancellationToken ct)
        {
            var settings = await db.ShopSettings.FirstAsync(s => s.Id == 1, ct);

            settings.MetaPixelId = UpdateMarketingSettingsDto.Normalize(dto.MetaPixelId);
            settings.TikTokPixelId = UpdateMarketingSettingsDto.Normalize(dto.TikTokPixelId);
            settings.MetaCapiToken = UpdateMarketingSettingsDto.NextToken(dto.MetaCapiToken, settings.MetaCapiToken);
            settings.TikTokEventsToken = UpdateMarketingSettingsDto.NextToken(dto.TikTokEventsToken, settings.TikTokEventsToken);
            settings.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return TypedResults.Ok(AdminView(settings));
        }

        public static async Task<IResult> GetPublic(ApplicationDbContext db, CancellationToken ct)
        {
            var settings = await db.ShopSettings.AsNoTracking().FirstAsync(s => s.Id == 1, ct);
            return TypedResults.Ok(new MarketingConfigDto(settings.MetaPixelId, settings.TikTokPixelId));
        }

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                app.MapGet("/api/marketing/config",
                        (HttpContext http, ApplicationDbContext db, CancellationToken ct) =>
                        {
                            // The site must see a cleared or changed ID at once, so every read is checked with the API.
                            http.Response.Headers.CacheControl = "no-cache";
                            return GetPublic(db, ct);
                        })
                    .AllowAnonymous();

                var admin = app.MapGroup("/api/admin/marketing/settings").RequireAuthorization(Policies.Admin);
                admin.MapGet("", GetAdmin);
                admin.MapPut("", Update).WithValidation<UpdateMarketingSettingsDto>();
            }
        }
    }
}
