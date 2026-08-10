using Microsoft.AspNetCore.Http.HttpResults;
using altinnendata_api.Features.Settings;
using altinnendata_api.Models.Admin;
using Xunit;

namespace altinnendata_api.Tests;

public class SettingsSliceTests : TestBase
{
    private static SettingsBody Body(
        string recipient = "shop@altinnendata.no",
        string name = "My Shop",
        string legalName = "",
        string orgNumber = "999 888 777",
        bool vat = false,
        string address = "New St 1, 0001 Oslo",
        string email = "post@altinnendata.no",
        string phone = "+47 473 88 759") =>
        new(recipient, name, legalName, orgNumber, vat, address, email, phone);

    [Fact]
    public async Task Get_DefaultsWhenNoRow()
    {
        await using var db = CreateDbContext();
        var ok = Assert.IsType<Ok<SettingsBody>>(await Settings.Get(db, default));
        Assert.Equal("sonyslyst@gmail.com", ok.Value!.ContactRecipientEmail);
        Assert.Equal("altinnendata@gmail.com", ok.Value.PublicEmail);
    }

    [Fact]
    public async Task Update_PersistsFields()
    {
        await using var db = CreateDbContext();
        db.AppSettings.Add(new AppSettings());
        await db.SaveChangesAsync();

        var ok = Assert.IsType<Ok<SettingsBody>>(await Settings.Update(Body(), db, default));
        Assert.Equal("shop@altinnendata.no", ok.Value!.ContactRecipientEmail);
        Assert.Equal("New St 1, 0001 Oslo", ok.Value.Address);

        var stored = await db.AppSettings.FindAsync(1);
        Assert.Equal("shop@altinnendata.no", stored!.ContactRecipientEmail);
        Assert.Equal("New St 1, 0001 Oslo", stored.Address);
        Assert.Equal("My Shop", stored.CompanyName);
        Assert.Equal("999 888 777", stored.OrgNumber);
        Assert.Equal("post@altinnendata.no", stored.PublicEmail);
        Assert.False(stored.VatRegistered);
    }

    [Fact]
    public async Task Get_ReturnsCompanyAndVatFields()
    {
        await using var db = CreateDbContext();
        db.AppSettings.Add(new AppSettings { CompanyName = "My Shop", OrgNumber = "111 222 333", VatRegistered = true });
        await db.SaveChangesAsync();

        var ok = Assert.IsType<Ok<SettingsBody>>(await Settings.Get(db, default));
        Assert.Equal("My Shop", ok.Value!.CompanyName);
        Assert.Equal("111 222 333", ok.Value.OrgNumber);
        Assert.True(ok.Value.VatRegistered);
    }

    [Fact]
    public void Validator_RequiresOrgNumberWhenVatRegistered()
    {
        var validator = new SettingsValidator();

        Assert.False(validator.Validate(Body(orgNumber: "", vat: true)).IsValid);
        Assert.True(validator.Validate(Body(orgNumber: "", vat: false)).IsValid);
    }
}
