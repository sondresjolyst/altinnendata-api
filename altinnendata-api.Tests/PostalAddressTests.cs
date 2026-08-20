using altinnendata_api.Helpers;
using Xunit;

namespace altinnendata_api.Tests;

public class PostalAddressTests
{
    [Fact]
    public void Format_JoinsStreetPostcodeAndPlace()
    {
        Assert.Equal("Mårvegen 21a, 4347 Lye", PostalAddress.Format("Mårvegen 21a", "4347", "Lye"));
    }

    [Fact]
    public void Format_SkipsPartsThatAreNotFilledIn()
    {
        Assert.Equal("Mårvegen 21a", PostalAddress.Format("Mårvegen 21a", "", ""));
        Assert.Equal("Mårvegen 21a, Lye", PostalAddress.Format("Mårvegen 21a", "", "Lye"));
        Assert.Equal("4347 Lye", PostalAddress.Format("", "4347", "Lye"));
        Assert.Equal("", PostalAddress.Format("", "", ""));
    }
}
