using Microsoft.EntityFrameworkCore;
using altinnendata_api.Models;
using altinnendata_api.Models.Shop;

namespace altinnendata_api.Features.Shop
{
    /// <summary>Adds the settings row and the three delivery options when missing. Never overwrites an edit.</summary>
    public static class ShopSeed
    {
        public static async Task EnsureAsync(ApplicationDbContext db, CancellationToken ct)
        {
            if (!await db.ShopSettings.AnyAsync(s => s.Id == 1, ct))
                db.ShopSettings.Add(new ShopSettings());

            var existing = await db.DeliveryOptions.Select(o => o.Method).ToListAsync(ct);

            foreach (var option in Defaults().Where(o => !existing.Contains(o.Method)))
                db.DeliveryOptions.Add(option);

            await db.SaveChangesAsync(ct);
        }

        private static IEnumerable<DeliveryOption> Defaults()
        {
            yield return new DeliveryOption { Method = DeliveryMethod.Shipping, Enabled = false, PriceNok = 0, LabelNo = "Sending", LabelEn = "Shipping" };
            yield return new DeliveryOption { Method = DeliveryMethod.HomeDelivery, Enabled = false, PriceNok = 0, LabelNo = "Levering hjem", LabelEn = "Home delivery", MaxDistanceKm = 30 };
            yield return new DeliveryOption { Method = DeliveryMethod.Pickup, Enabled = true, PriceNok = 0, LabelNo = "Henting", LabelEn = "Pickup" };
        }
    }
}
