using System.Reflection;
using Microsoft.Extensions.Caching.Memory;
using SjolystInnovation.AppKit.Vipps;
using altinnendata_api.Models;

namespace altinnendata_api.Features.Shop
{
    /// <summary>
    /// Sends new payments to the Vipps test environment while test mode is on in the shop settings.
    /// A missing settings row counts as test. Read for at most 30 seconds from the cache.
    /// </summary>
    public class ShopVippsEnvironment(IMemoryCache cache, IServiceScopeFactory scopes) : IVippsEnvironment
    {
        public async Task<bool> IsTestAsync(CancellationToken cancellationToken = default)
        {
            if (cache.TryGetValue(ShopSettingsEndpoints.VippsTestModeCacheKey, out bool cached))
                return cached;

            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var settings = await db.ShopSettings.FindAsync([1], cancellationToken);
            var isTest = settings?.VippsTestMode ?? true;
            cache.Set(ShopSettingsEndpoints.VippsTestModeCacheKey, isTest, TimeSpan.FromSeconds(30));
            return isTest;
        }
    }

    public static class ShopVipps
    {
        /// <summary>How long one Vipps HTTP call may take. Checkout holds the build's row lock while it waits.</summary>
        public static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(15);

        /// <summary>Registers the Vipps client with the credentials in the <c>Vipps</c> section and the shop's test mode.</summary>
        public static IServiceCollection AddShopVipps(this IServiceCollection services, IConfiguration config)
        {
            services.AddSingleton<IVippsEnvironment, ShopVippsEnvironment>();
            services.AddAppKitVipps(config.GetSection("Vipps"), o =>
                {
                    o.SystemName = "altinnendata";
                    o.SystemVersion = SystemVersion();
                })
                .ConfigureHttpClient(c => c.Timeout = HttpTimeout);
            return services;
        }

        private static string SystemVersion() =>
            typeof(ShopVipps).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "1.0.0";
    }
}
