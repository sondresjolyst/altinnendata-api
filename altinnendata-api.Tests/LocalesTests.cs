using altinnendata_api.Constants;
using Xunit;

namespace altinnendata_api.Tests;

public class LocalesTests
{
    [Theory]
    [InlineData("no", "no")]
    [InlineData("NO", "no")]
    [InlineData("nb", "no")]
    [InlineData("nb-NO", "no")]
    [InlineData("nn", "no")]
    [InlineData("en", "en")]
    [InlineData("en-GB", "en")]
    [InlineData("de", "no")]
    [InlineData("", "no")]
    [InlineData(null, "no")]
    public void Normalize_MapsToSupportedLocale(string? input, string expected) =>
        Assert.Equal(expected, Locales.Normalize(input));

    [Theory]
    [InlineData("no", true)]
    [InlineData("en", true)]
    [InlineData("nb", false)]
    [InlineData(null, false)]
    public void IsSupported_OnlyAcceptsExactTags(string? input, bool expected) =>
        Assert.Equal(expected, Locales.IsSupported(input));
}
