using altinnendata_api.Features.Finn;
using Xunit;

namespace altinnendata_api.Tests;

public class FinnUrlsTests
{
    [Theory]
    [InlineData("https://www.finn.no/recommerce/forsale/item/412345678", true)]
    [InlineData("https://finn.no/item/1", true)]
    [InlineData("http://www.finn.no/item/1", false)]
    [InlineData("https://finn.no.evil.example/item/1", false)]
    [InlineData("https://evil.example/?x=finn.no", false)]
    [InlineData("http://169.254.169.254/latest/meta-data/", false)]
    [InlineData("file:///etc/passwd", false)]
    [InlineData(null, false)]
    public void IsAdUrl_OnlyAcceptsHttpsFinn(string? url, bool expected) =>
        Assert.Equal(expected, FinnUrls.IsAdUrl(url));

    [Theory]
    [InlineData("https://images.finncdn.no/dynamic/1280w/2026/8/vertical-0/11/abc.jpg", true)]
    [InlineData("https://www.finn.no/image.jpg", true)]
    [InlineData("https://images.example.com/a.jpg", false)]
    [InlineData("http://images.finncdn.no/a.jpg", false)]
    public void IsImageUrl_OnlyAcceptsFinnHosts(string url, bool expected) =>
        Assert.Equal(expected, FinnUrls.IsImageUrl(url));
}

public class FinnAdParserTests
{
    private const string Html = """
        <html><head>
        <title>Gaming PC til salgs</title>
        <meta property="og:title" content="Gaming-PC med RTX 5070" />
        <meta property="og:description" content="Nesten ny maskin, lite brukt." />
        <meta property="og:image" content="https://images.finncdn.no/dynamic/1280w/first.jpg" />
        <script type="application/ld+json">
        {"@type":"Product","name":"Gaming-PC","image":["https://images.finncdn.no/dynamic/1280w/first.jpg","https://images.finncdn.no/dynamic/1280w/second.jpg"],"offers":{"@type":"Offer","price":18990,"priceCurrency":"NOK"}}
        </script>
        </head><body></body></html>
        """;

    [Fact]
    public void ReadsTitleDescriptionAndPrice()
    {
        var ad = FinnAdParser.Parse(Html);

        Assert.Equal("Gaming-PC med RTX 5070", ad.Title);
        Assert.Equal("Nesten ny maskin, lite brukt.", ad.Description);
        Assert.Equal(18990, ad.PriceNok);
    }

    [Fact]
    public void CollectsGalleryImagesWithoutRepeatingTheCover()
    {
        var ad = FinnAdParser.Parse(Html);

        Assert.Equal(
            ["https://images.finncdn.no/dynamic/1280w/first.jpg", "https://images.finncdn.no/dynamic/1280w/second.jpg"],
            ad.ImageUrls);
    }

    [Fact]
    public void FallsBackToTheTitleTagAndSurvivesMissingJsonLd()
    {
        var ad = FinnAdParser.Parse("<html><head><title>Bare en tittel</title></head><body></body></html>");

        Assert.Equal("Bare en tittel", ad.Title);
        Assert.Null(ad.Description);
        Assert.Null(ad.PriceNok);
        Assert.Empty(ad.ImageUrls);
    }

    [Fact]
    public void IgnoresAMalformedJsonLdBlock()
    {
        var html = """
            <html><head><meta property="og:title" content="Tittel" />
            <script type="application/ld+json">{ not json }</script>
            </head></html>
            """;

        var ad = FinnAdParser.Parse(html);

        Assert.Equal("Tittel", ad.Title);
    }

    [Fact]
    public void DecodesHtmlEntitiesInText()
    {
        var html = """<html><head><meta property="og:title" content="PC &amp; skjerm" /></head></html>""";

        Assert.Equal("PC & skjerm", FinnAdParser.Parse(html).Title);
    }
}
