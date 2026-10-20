using Microsoft.EntityFrameworkCore;
using altinnendata_api.Models;

namespace altinnendata_api.Features.Shop
{
    /// <summary>
    /// Holds a build for the order that started checkout, and lets go of it again.
    /// A hold is only ever changed by the order that owns it, so an admin's own edit always wins.
    /// </summary>
    public static class BuildHold
    {
        /// <summary>
        /// Reserves the build for <paramref name="orderId"/> in a single UPDATE, so two orders racing
        /// for the same build cannot both win. Returns false when the build cannot be bought, which includes a build
        /// with a part that has no condition.
        /// </summary>
        public static Task<bool> TryHoldAsync(ApplicationDbContext db, int buildId, int orderId, int quantity, CancellationToken ct)
        {
            // One of each build exists. Stock would replace the WHERE below with a quantity decrement.
            ArgumentOutOfRangeException.ThrowIfNotEqual(quantity, 1);
            return HoldAsync(db, buildId, orderId, ct);
        }

        private static async Task<bool> HoldAsync(ApplicationDbContext db, int buildId, int orderId, CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var rows = await db.PcBuilds
                .Where(b => b.Id == buildId
                    && b.Availability == BuildAvailability.Available
                    && b.Published
                    && b.PriceNok != null
                    && b.HeldByOrderId == null
                    && b.Components.Any()
                    && b.Components.All(c => c.ComponentConditionId != null))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.Availability, BuildAvailability.Reserved)
                    .SetProperty(b => b.HeldByOrderId, orderId)
                    .SetProperty(b => b.UpdatedAt, now), ct);
            return rows == 1;
        }

        /// <summary>Makes the build available again, but only while <paramref name="orderId"/> still holds it.</summary>
        public static bool Release(PcBuild build, int orderId)
        {
            if (build.HeldByOrderId != orderId || build.Availability != BuildAvailability.Reserved) return false;

            build.Availability = BuildAvailability.Available;
            build.HeldByOrderId = null;
            build.UpdatedAt = DateTime.UtcNow;
            return true;
        }

        /// <summary>Marks the build sold on <paramref name="today"/>, but only while <paramref name="orderId"/> holds it.</summary>
        public static bool MarkSold(PcBuild build, int orderId, DateOnly today)
        {
            if (build.HeldByOrderId != orderId) return false;

            build.Availability = BuildAvailability.Sold;
            build.SoldOn = today;
            build.HeldByOrderId = null;
            build.UpdatedAt = DateTime.UtcNow;
            return true;
        }
    }
}
