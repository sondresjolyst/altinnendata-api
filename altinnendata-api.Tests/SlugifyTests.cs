using altinnendata_api.Helpers;
using Xunit;

namespace altinnendata_api.Tests;

public class SlugifyTests
{
    [Theory]
    [InlineData("Volvo 242 Turbo", "volvo-242-turbo")]
    [InlineData("  Spaced   Out  ", "spaced-out")]
    [InlineData("Audi RS6 / 600hp!", "audi-rs6-600hp")]
    [InlineData("Blåbær Æøå", "blabaer-aeoa")]
    public void Create_ProducesExpectedSlug(string input, string expected)
    {
        Assert.Equal(expected, Slugify.Create(input));
    }

    [Fact]
    public void Create_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, Slugify.Create("   "));
    }
}
