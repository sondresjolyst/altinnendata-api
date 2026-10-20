namespace altinnendata_api.Services
{
    /// <summary>Builds absolute links back into the website for emails.</summary>
    public static class SiteLinks
    {
        private const string Fallback = "https://www.altinnendata.no";
        private const string DefaultLocale = "no";

        public static string BaseUrl(IConfiguration config) =>
            (config["Site:BaseUrl"] ?? Fallback).TrimEnd('/');

        public static string Build(IConfiguration config, string slug) =>
            $"{BaseUrl(config)}/{DefaultLocale}/builds/{Uri.EscapeDataString(slug)}";

        public static string SetPasswordPage(IConfiguration config) =>
            $"{BaseUrl(config)}/{DefaultLocale}/reset-password";

        /// <summary>The page Vipps sends the buyer back to after paying.</summary>
        public static string ShopReturn(IConfiguration config, string locale, string token) =>
            $"{BaseUrl(config)}/{locale}/shop/return?token={Uri.EscapeDataString(token)}";

        /// <summary>The page where the buyer pays an order again after its reservation ended.</summary>
        public static string ShopPay(IConfiguration config, string locale, string token) =>
            $"{BaseUrl(config)}/{locale}/shop/pay/{Uri.EscapeDataString(token)}";

        public static string AdminOrder(IConfiguration config, int id) =>
            $"{BaseUrl(config)}/{DefaultLocale}/admin/shop/orders/{id}";

        public static string Terms(IConfiguration config, string locale) =>
            $"{BaseUrl(config)}/{locale}/terms";
    }
}
