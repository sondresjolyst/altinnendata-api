using Microsoft.Extensions.Configuration;
using altinnendata_api.Services;
using Xunit;

namespace altinnendata_api.Tests;

public class SiteLinksTests
{
    private static IConfiguration Config(string? baseUrl) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Site:BaseUrl"] = baseUrl })
            .Build();

    [Fact]
    public void SetPassword_PointsAtTheConfiguredEnvironment()
    {
        var link = SiteLinks.SetPasswordPage(Config("https://dev.altinnendata.no"));

        Assert.Equal("https://dev.altinnendata.no/no/reset-password", link);
    }

    [Fact]
    public void SetPassword_TrimsATrailingSlash()
    {
        var link = SiteLinks.SetPasswordPage(Config("https://www.altinnendata.no/"));

        Assert.Equal("https://www.altinnendata.no/no/reset-password", link);
    }

    [Fact]
    public void SetPassword_FallsBackToProductionWhenUnset()
    {
        var link = SiteLinks.SetPasswordPage(Config(null));

        Assert.Equal("https://www.altinnendata.no/no/reset-password", link);
    }

    [Fact]
    public void ShopReturn_CarriesTheLocaleAndTheToken()
    {
        var link = SiteLinks.ShopReturn(Config("https://www.altinnendata.no/"), "en", "0123abcd");

        Assert.Equal("https://www.altinnendata.no/en/shop/return?token=0123abcd", link);
    }

    [Fact]
    public void AdminOrder_PointsAtTheNorwegianAdminPage()
    {
        var link = SiteLinks.AdminOrder(Config("https://dev.altinnendata.no"), 42);

        Assert.Equal("https://dev.altinnendata.no/no/admin/shop/orders/42", link);
    }

    [Fact]
    public void Terms_UsesTheLocale()
    {
        var link = SiteLinks.Terms(Config(null), "en");

        Assert.Equal("https://www.altinnendata.no/en/terms", link);
    }

    [Fact]
    public void AdminEnquiry_PointsAtTheAdminPage()
    {
        var link = SiteLinks.AdminEnquiry(Config("https://dev.altinnendata.no/"), 7);

        Assert.Equal("https://dev.altinnendata.no/no/admin/enquiries/7", link);
    }
}
