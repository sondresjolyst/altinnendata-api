using System.Text;
using altinnendata_api.Models.Admin;
using altinnendata_api.Services;
using Xunit;

namespace altinnendata_api.Tests;

public class EmailFooterTests
{
    private static string Footer(AppSettings s, bool norwegian)
    {
        var body = new StringBuilder();
        EmailFooter.Append(body, s, norwegian);
        return body.ToString();
    }

    private static AppSettings Registered() => new()
    {
        CompanyName = "Altinnendata",
        CompanyLegalName = "ALTINNENDATA AS",
        OrgNumber = "938 433 240",
        StreetAddress = "Mårvegen 21a",
        PostalCode = "4347",
        AddressLocality = "Lye",
        PublicEmail = "altinnendata@gmail.com",
    };

    [Fact]
    public void Norwegian_NamesTheRegisterAfterTheOrgNumber_AndTheVatStatus()
    {
        var html = Footer(Registered(), norwegian: true);

        Assert.Contains("<strong>ALTINNENDATA AS</strong><br/>Org.nr. 938 433 240<br/>Foretaksregisteret<br/>", html);
        Assert.Contains("M&#229;rvegen 21a", html);
        Assert.Contains("4347 Lye", html);
        Assert.Contains("altinnendata@gmail.com", html);
        Assert.EndsWith("<br/>Ikke registrert i Merverdiavgiftsregisteret</p>", html);
    }

    [Fact]
    public void English_UsesTheEnglishNames()
    {
        var html = Footer(Registered(), norwegian: false);

        Assert.Contains("Org. no. 938 433 240<br/>Register of Business Enterprises<br/>", html);
        Assert.Contains("Not registered in the VAT Register", html);
        Assert.DoesNotContain("Foretaksregisteret", html);
    }

    [Fact]
    public void WithoutAnOrgNumber_LeavesOutTheNumberAndTheRegister_AndFallsBackToTheTradingName()
    {
        var html = Footer(new AppSettings(), norwegian: true);

        Assert.Contains("<strong>Altinnendata</strong>", html);
        Assert.DoesNotContain("Org.nr.", html);
        Assert.DoesNotContain("Foretaksregisteret", html);
    }

    [Fact]
    public void VatRegistered_LeavesOutTheVatLine()
    {
        var s = Registered();
        s.VatRegistered = true;

        Assert.DoesNotContain("Merverdiavgiftsregisteret", Footer(s, norwegian: true));
        Assert.DoesNotContain("VAT Register", Footer(s, norwegian: false));
    }

    [Fact]
    public void EncodesTheSettings()
    {
        var s = Registered();
        s.CompanyLegalName = "<b>A&B AS</b>";

        Assert.Contains("&lt;b&gt;A&amp;B AS&lt;/b&gt;", Footer(s, norwegian: true));
    }
}
