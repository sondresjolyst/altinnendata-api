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
}
