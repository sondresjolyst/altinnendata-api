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
        var link = SiteLinks.SetPassword(Config("https://dev.altinnendata.no"), "ny@altinnendata.no", "YB71WN");

        Assert.Equal("https://dev.altinnendata.no/no/reset-password?email=ny%40altinnendata.no&code=YB71WN", link);
    }

    [Fact]
    public void SetPassword_TrimsATrailingSlash()
    {
        var link = SiteLinks.SetPassword(Config("https://www.altinnendata.no/"), "a@b.no", "ABC123");

        Assert.StartsWith("https://www.altinnendata.no/no/reset-password?", link);
    }

    [Fact]
    public void SetPassword_FallsBackToProductionWhenUnset()
    {
        var link = SiteLinks.SetPassword(Config(null), "a@b.no", "ABC123");

        Assert.StartsWith("https://www.altinnendata.no/no/reset-password?", link);
    }
}
