using System.Reflection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using SjolystInnovation.AppKit.Vipps;
using altinnendata_api.Infrastructure;
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
        /// <summary>The name of the shop's ePayment webhook.</summary>
        public const string Slot = "shop";

        /// <summary>The path Vipps sends the shop's ePayment events to.</summary>
        public const string WebhookPath = "/api/shop/webhook";

        /// <summary>The Postgres advisory lock that lets one instance register the webhook at a time.</summary>
        public const long AdvisoryLockKey = 7300115;

        /// <summary>
        /// Registers the Vipps client with the credentials in the <c>Vipps</c> section and the shop's test mode,
        /// the ePayment webhook and the payment polling that stands in for lost webhooks. The Data Protection keys that protect the webhook secrets are kept in the database, encrypted with the configured certificate.
        /// </summary>
        public static IServiceCollection AddShopVipps(this IServiceCollection services, IConfiguration config, bool requireKeyCertificate)
        {
            services.AddDataProtection()
                .PersistKeysToDbContext<ApplicationDbContext>()
                .SetApplicationName("altinnendata-api")
                .ProtectKeysWithConfiguredCertificate(config, requireKeyCertificate);
            services.AddSingleton<IVippsEnvironment, ShopVippsEnvironment>();
            services.AddAppKitVipps(config.GetSection("Vipps"), o =>
                {
                    o.SystemName = "altinnendata";
                    o.SystemVersion = SystemVersion();
                })
                // Checkout holds the build's row lock while a Vipps call waits, so the call gets a short timeout.
                .ConfigureHttpClient(c => c.Timeout = VippsFailures.RecommendedTimeout);
            services.AddAppKitVippsWebhooks<ApplicationDbContext>(o =>
            {
                o.PublicBaseUrl = config["Api:PublicBaseUrl"] ?? "";
                o.AddSlot(Slot, VippsWebhookKind.Payment, WebhookPath);
                o.AdvisoryLockKey = AdvisoryLockKey;
            });
            services.AddScoped<ShopWebhookHandler>();
            services.AddScoped<IVippsPaymentWebhookHandler>(sp => sp.GetRequiredService<ShopWebhookHandler>());
            services.AddScoped<ShopPaymentPolling>();
            services.AddHostedService<ShopPaymentPollingService>();
            return services;
        }

        private static string SystemVersion() =>
            typeof(ShopVipps).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "1.0.0";
    }
}
