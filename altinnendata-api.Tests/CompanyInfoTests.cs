using Microsoft.AspNetCore.Http.HttpResults;
using altinnendata_api.Features.Content;
using altinnendata_api.Models.Admin;
using Xunit;

namespace altinnendata_api.Tests;

public class CompanyInfoTests : TestBase
{
    [Fact]
    public async Task Get_ReturnsDefaultsWhenNoRow()
    {
        await using var db = CreateDbContext();
        var ok = Assert.IsType<Ok<CompanyInfoResponse>>(await CompanyInfo.Get(db, default));
        Assert.Equal("Altinnendata", ok.Value!.Name);
        Assert.False(string.IsNullOrWhiteSpace(ok.Value!.Address));
    }

    [Fact]
    public async Task Get_ReturnsAddressAsPartsAndAsOneLine()
    {
        await using var db = CreateDbContext();
        db.AppSettings.Add(new AppSettings
        {
            StreetAddress = "New St 1",
            PostalCode = "0001",
            AddressLocality = "Oslo",
            AddressRegion = "Oslo",
        });
        await db.SaveChangesAsync();

        var ok = Assert.IsType<Ok<CompanyInfoResponse>>(await CompanyInfo.Get(db, default));
        Assert.Equal("New St 1, 0001 Oslo", ok.Value!.Address);
        Assert.Equal("New St 1", ok.Value.StreetAddress);
        Assert.Equal("0001", ok.Value.PostalCode);
        Assert.Equal("Oslo", ok.Value.AddressLocality);
        Assert.Equal("Oslo", ok.Value.AddressRegion);
    }

    [Fact]
    public async Task Get_OmitsThePlaceFromTheOneLineWhenItIsNotSet()
    {
        await using var db = CreateDbContext();
        db.AppSettings.Add(new AppSettings { StreetAddress = "New St 1", PostalCode = "", AddressLocality = "" });
        await db.SaveChangesAsync();

        var ok = Assert.IsType<Ok<CompanyInfoResponse>>(await CompanyInfo.Get(db, default));
        Assert.Equal("New St 1", ok.Value!.Address);
    }

    [Fact]
    public async Task Get_ExposesOrgNumberAndVatStatus()
    {
        await using var db = CreateDbContext();
        db.AppSettings.Add(new AppSettings { OrgNumber = "111 222 333", VatRegistered = true });
        await db.SaveChangesAsync();

        var ok = Assert.IsType<Ok<CompanyInfoResponse>>(await CompanyInfo.Get(db, default));
        Assert.Equal("111 222 333", ok.Value!.OrgNumber);
        Assert.True(ok.Value!.VatRegistered);
    }
}
