using altinnendata_api.Models.Admin;
using altinnendata_api.Services;
using Xunit;

namespace altinnendata_api.Tests;

public class AuthEmailBrandTests : TestBase
{
    [Fact]
    public async Task GetBrandAsync_PassesTheRawOrgNumberAndTheVatFlag()
    {
        await using var db = CreateDbContext();
        db.AppSettings.Add(new AppSettings
        {
            Id = 1,
            CompanyName = "Altinnendata",
            CompanyLegalName = "Altinnendata AS",
            OrgNumber = "923202374",
            VatRegistered = true,
            StreetAddress = "Gate 1",
            PostalCode = "0150",
            AddressLocality = "Oslo",
            PublicEmail = "post@example.com",
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var brand = await new AuthEmailBrand(db).GetBrandAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Altinnendata", brand.Name);
        Assert.Equal("Altinnendata AS", brand.LegalName);
        Assert.Equal("923202374", brand.OrgNumber);
        Assert.True(brand.VatRegistered);
        Assert.Equal("post@example.com", brand.Email);
        Assert.Equal("Foretaksregisteret", brand.RegisterName);
        Assert.Equal(new AppSettings { StreetAddress = "Gate 1", PostalCode = "0150", AddressLocality = "Oslo" }.FormattedAddress, brand.Address);
    }

    [Fact]
    public async Task GetBrandAsync_WithoutVat_LeavesTheFlagOff()
    {
        await using var db = CreateDbContext();
        db.AppSettings.Add(new AppSettings { Id = 1, OrgNumber = "923202374", VatRegistered = false });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var brand = await new AuthEmailBrand(db).GetBrandAsync(TestContext.Current.CancellationToken);

        Assert.Equal("923202374", brand.OrgNumber);
        Assert.False(brand.VatRegistered);
    }
}
