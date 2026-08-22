using Microsoft.AspNetCore.Http.HttpResults;
using altinnendata_api.Features.Meta;
using Xunit;

namespace altinnendata_api.Tests;

public class RobotsTxtTests
{
    [Fact]
    public void Get_DisallowsEverythingForEveryCrawler()
    {
        var result = Assert.IsType<ContentHttpResult>(RobotsTxt.Get());

        Assert.Equal("User-agent: *\nDisallow: /\n", result.ResponseContent);
        Assert.Equal("text/plain; charset=utf-8", result.ContentType);
    }

    [Fact]
    public void Get_GrantsNoExceptions()
    {
        var result = Assert.IsType<ContentHttpResult>(RobotsTxt.Get());

        Assert.DoesNotContain("Allow:", result.ResponseContent!);
    }
}
