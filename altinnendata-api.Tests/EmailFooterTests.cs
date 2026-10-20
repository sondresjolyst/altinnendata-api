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
    public void Norwegian_NamesTheRegisterAfterTheOrgNumber_WithoutAVatLine()
    {
        var html = Footer(Registered(), norwegian: true);

        Assert.Contains("<strong>ALTINNENDATA AS</strong><br/>Org.nr. 938 433 240 Foretaksregisteret<br/>", html);
        Assert.Contains("M&#229;rvegen 21a", html);
        Assert.Contains("4347 Lye", html);
        Assert.EndsWith("<br/>altinnendata@gmail.com</p>", html);
        Assert.DoesNotContain("Merverdiavgiftsregisteret", html);
        Assert.DoesNotContain("MVA", html);
    }

    [Fact]
    public void English_KeepsTheRegisterName()
    {
        var html = Footer(Registered(), norwegian: false);

        Assert.Contains("<br/>Org. no. 938 433 240 Foretaksregisteret<br/>", html);
        Assert.DoesNotContain("VAT", html);
    }

    [Fact]
    public void WithoutAnOrgNumber_LeavesOutTheNumberAndTheRegister_AndFallsBackToTheTradingName()
    {
        var html = Footer(new AppSettings(), norwegian: true);

        Assert.Contains("<strong>Altinnendata</strong>", html);
        Assert.DoesNotContain("Org.nr.", html);
        Assert.DoesNotContain("Foretaksregisteret", html);
    }

    [Theory]
    [InlineData(true, "Org.nr. 938 433 240 MVA Foretaksregisteret")]
    [InlineData(false, "Org. no. 938 433 240 MVA Foretaksregisteret")]
    public void VatRegistered_AddsMvaToTheOrgNumber(bool norwegian, string line)
    {
        var s = Registered();
        s.VatRegistered = true;

        Assert.Contains($"<br/>{line}<br/>", Footer(s, norwegian));
    }

    [Fact]
    public void EncodesTheSettings()
    {
        var s = Registered();
        s.CompanyLegalName = "<b>A&B AS</b>";

        Assert.Contains("&lt;b&gt;A&amp;B AS&lt;/b&gt;", Footer(s, norwegian: true));
    }
}
