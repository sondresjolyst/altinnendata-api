using altinnendata_api.Features.Finn;
using Xunit;

namespace altinnendata_api.Tests;

public class FinnGalleryTests
{
    private const string AdUrl = "https://www.finn.no/recommerce/forsale/item/123456789";

    private static string Photo(string name, int width) =>
        $"https://images.finncdn.no/dynamic/{width}w/2026/1/vertical-0/01/1/123/456/789_{name}.jpg";

    [Fact]
    public void KeepsOnePerPhotoAtTheLargestWidth()
    {
        var html = $"""
            <html><head><meta property="og:image" content="{Photo("aaa", 1280)}" /></head>
            <body>
            <img src="{Photo("aaa", 320)}" srcset="{Photo("aaa", 640)} 640w, {Photo("aaa", 1600)} 1600w" />
            <img src="{Photo("bbb", 320)}" srcset="{Photo("bbb", 1600)} 1600w" />
            </body></html>
            """;

        var ad = FinnAdParser.Parse(html, AdUrl);

        Assert.Equal([Photo("aaa", 1600), Photo("bbb", 1600)], ad.ImageUrls);
    }

    private static string ItemPhoto(string name, int width) =>
        $"https://images.finncdn.no/dynamic/{width}w/item/123456789/{name}";

    [Fact]
    public void ReadsTheNewerItemUrlShapeWithoutAFileExtension()
    {
        var html = $"""
            <html><body>
            <div style="background-image:url({ItemPhoto("1111aaaa-1111-2222-3333-444455556666", 142)});aspect-ratio:3"></div>
            <img src="{ItemPhoto("1111aaaa-1111-2222-3333-444455556666", 1280)}" />
            <img src="{ItemPhoto("2222bbbb-1111-2222-3333-444455556666", 1280)}" />
            </body></html>
            """;

        var ad = FinnAdParser.Parse(html, AdUrl);

        Assert.Equal(
            [ItemPhoto("1111aaaa-1111-2222-3333-444455556666", 1280), ItemPhoto("2222bbbb-1111-2222-3333-444455556666", 1280)],
            ad.ImageUrls);
    }

    [Fact]
    public void IgnoresPhotosBelongingToOtherAdverts()
    {
        var html = $"""
            <html><body>
            <img src="{Photo("mine", 1280)}" />
            <img src="https://images.finncdn.no/dynamic/1280w/2026/1/vertical-0/01/1/987/654/321_other.jpg" />
            <img src="https://images.finncdn.no/dynamic/960w/free-shipping-v2.jpg" />
            </body></html>
            """;

        var ad = FinnAdParser.Parse(html, AdUrl);

        Assert.Equal([Photo("mine", 1280)], ad.ImageUrls);
    }

    [Theory]
    [InlineData("Kontor-PC med skjerm | FINN-torget", "Kontor-PC med skjerm")]
    [InlineData("Gaming-PC - FINN torget", "Gaming-PC")]
    [InlineData("Gaming-PC med RTX 5070", "Gaming-PC med RTX 5070")]
    [InlineData("PC | tastatur | FINN-torget", "PC | tastatur")]
    public void StripsTheFinnSuffixFromTheTitle(string raw, string expected)
    {
        var html = $"""<html><head><meta property="og:title" content="{raw}" /></head></html>""";

        Assert.Equal(expected, FinnAdParser.Parse(html).Title);
    }

    [Fact]
    public void IgnoresFinnsOwnLogosAndPlaceholders()
    {
        var html = $$"""
            <html><body>
            <script type="application/ld+json">
            {"@type":"Organization","image":"https://static.finncdn.no/_c/static/FINN-thumbnail-290x290.jpg"}
            </script>
            <img src="{{Photo("mine", 1280)}}" />
            </body></html>
            """;

        var ad = FinnAdParser.Parse(html, AdUrl);

        Assert.Equal([Photo("mine", 1280)], ad.ImageUrls);
    }

    [Fact]
    public void ReadsTheDescriptionFromTheAdvertBodyRatherThanTheShortenedMetaTag()
    {
        var html = """
            <html><head>
            <meta property="og:description" content="Testet \u{d83d}\u{dd0c}
            Ny installasjon av Windows 11" />
            </head><body>
            <section data-testid="description">
            <div><p>Testet \u{d83d}\u{dd0c}</p><p> </p><p>Ny installasjon av Windows 11</p>
            <p>Helt klar til bruk \u{2728}</p></div>
            <div class="hidden"><w-button data-testid="toggle-description"><w-icon name="Plus"></w-icon> Vis hele beskrivelsen</w-button>
            <span class="sr-only">NB: Knappen har kun en visuell effekt.</span></div>
            </section>
            </body></html>
            """;

        Assert.Equal(
            "Testet \U0001F50C\n\nNy installasjon av Windows 11\n\nHelt klar til bruk ✨",
            FinnAdParser.Parse(html).Description);
    }

    [Fact]
    public void FallsBackToTheMetaDescriptionWhenTheAdvertHasNoDescriptionSection()
    {
        var html = """<html><head><meta property="og:description" content="M&#229; hentes p&#229; Frogner." /></head></html>""";

        Assert.Equal("Må hentes på Frogner.", FinnAdParser.Parse(html).Description);
    }

    [Fact]
    public void DecodesEscapedEmojiInTheTitle()
    {
        var html = """<html><head><meta property="og:title" content="\u{2728}RTX 4070 \u{d83c}\u{dfae} | FINN-torget" /></head></html>""";

        Assert.Equal("✨RTX 4070 \U0001F3AE", FinnAdParser.Parse(html).Title);
    }

    [Fact]
    public void FallsBackToTheOpenGraphImageWhenTheUrlIsUnknown()
    {
        var html = $"""<html><head><meta property="og:image" content="{Photo("aaa", 1280)}" /></head></html>""";

        var ad = FinnAdParser.Parse(html);

        Assert.Equal([Photo("aaa", 1280)], ad.ImageUrls);
    }
}
