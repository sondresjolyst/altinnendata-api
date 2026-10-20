using altinnendata_api.Models.Admin;
using altinnendata_api.Services;
using SjolystInnovation.AppKit.Email;
using Xunit;

namespace altinnendata_api.Tests;

public class BrandedEmailTests
{
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

    private static string Html(AppSettings s, bool norwegian, EmailButton? button = null) =>
        BrandedEmail.Html(s, norwegian, "Sendt", "Ordren din er sendt", "<p>Hei</p>", button, button is null ? null : "<p>Etter</p>");

    [Fact]
    public void Norwegian_ShowsTheCompanyLines_WithTheRegisterAfterTheOrgNumber_InTheHeader()
    {
        var html = Html(Registered(), norwegian: true);

        Assert.Contains(">Altinnendata</div>", html);
        Assert.Contains(">ALTINNENDATA AS<br>Org.nr. 938 433 240 Foretaksregisteret<br>M&#229;rvegen 21a, 4347 Lye<br>altinnendata@gmail.com</div>", html);
        Assert.DoesNotContain("MVA", html);
        Assert.DoesNotContain("Merverdiavgiftsregisteret", html);
    }

    [Fact]
    public void English_KeepsTheRegisterName()
    {
        Assert.Contains("<br>Org. no. 938 433 240 Foretaksregisteret<br>", Html(Registered(), norwegian: false));
    }

    [Fact]
    public void WithoutAnOrgNumber_LeavesOutTheNumberAndTheRegister()
    {
        var html = Html(new AppSettings(), norwegian: true);

        Assert.Contains(">Altinnendata</div>", html);
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

        Assert.Contains($"<br>{line}<br>", Html(s, norwegian));
    }

    [Fact]
    public void EncodesTheSettings()
    {
        var s = Registered();
        s.CompanyLegalName = "<b>A&B AS</b>";

        var html = Html(s, norwegian: true);

        Assert.Contains("&lt;b&gt;A&amp;B AS&lt;/b&gt;", html);
        Assert.DoesNotContain("<b>A&B", html);
    }

    [Fact]
    public void UsesTheSiteColours_TheBadge_TheHeading_AndTheTextStyle()
    {
        var html = Html(Registered(), norwegian: true, new EmailButton("Betal med Vipps", "https://example.no/pay"));

        Assert.Contains("background:#0f3d3a;color:#fff", html);
        Assert.Contains("background:#00887a;color:#fff", html);
        Assert.Contains(">Sendt</span>", html);
        Assert.Contains(">Ordren din er sendt</h1>", html);
        Assert.Contains($"<div style=\"{EmailLayout.TextStyle}\"><p>Hei</p></div>", html);
        Assert.Contains($"<div style=\"{EmailLayout.TextStyle}\"><p>Etter</p></div>", html);
        Assert.True(html.IndexOf("Hei", StringComparison.Ordinal) < html.IndexOf(">Betal med Vipps</a>", StringComparison.Ordinal));
        Assert.True(html.IndexOf(">Betal med Vipps</a>", StringComparison.Ordinal) < html.IndexOf("Etter", StringComparison.Ordinal));
    }

    [Fact]
    public void AnEmptyBody_LeavesNoEmptyTextBlock()
    {
        var html = BrandedEmail.Html(Registered(), true, "Ny ordre", "Ny ordre", "", new EmailButton("Åpne ordren i admin", "https://example.no/admin"), "<p>Rader</p>");

        Assert.DoesNotContain($"<div style=\"{EmailLayout.TextStyle}\"></div>", html);
        Assert.True(html.IndexOf(">Ny ordre</h1>", StringComparison.Ordinal) < html.IndexOf("&#197;pne ordren i admin", StringComparison.Ordinal));
    }
}
