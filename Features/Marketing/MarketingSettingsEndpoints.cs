using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;

namespace altinnendata_api.Features.Marketing
{
    /// <summary>The admin's marketing settings and the public marketing configuration.</summary>
    public static class MarketingSettingsEndpoints
    {
        public static async Task<IResult> GetAdmin(ApplicationDbContext db, CancellationToken ct)
        {
            var settings = await db.ShopSettings.AsNoTracking().FirstAsync(s => s.Id == 1, ct);
            return TypedResults.Ok(new MarketingSettingsDto(settings.MetaPixelId));
        }

        public static async Task<IResult> Update(UpdateMarketingSettingsDto dto, ApplicationDbContext db, CancellationToken ct)
        {
            var settings = await db.ShopSettings.FirstAsync(s => s.Id == 1, ct);

            settings.MetaPixelId = UpdateMarketingSettingsDto.Normalize(dto.MetaPixelId);
            settings.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return TypedResults.Ok(new MarketingSettingsDto(settings.MetaPixelId));
        }

        public static async Task<IResult> GetPublic(ApplicationDbContext db, CancellationToken ct)
        {
            var settings = await db.ShopSettings.AsNoTracking().FirstAsync(s => s.Id == 1, ct);
            return TypedResults.Ok(new MarketingConfigDto(settings.MetaPixelId));
        }

        public class Endpoints : IEndpoint
        {
            public void Map(IEndpointRouteBuilder app)
            {
                app.MapGet("/api/marketing/config", GetPublic).AllowAnonymous();

                var admin = app.MapGroup("/api/admin/marketing/settings").RequireAuthorization(Policies.Admin);
                admin.MapGet("", GetAdmin);
                admin.MapPut("", Update).WithValidation<UpdateMarketingSettingsDto>();
            }
        }
    }
}
