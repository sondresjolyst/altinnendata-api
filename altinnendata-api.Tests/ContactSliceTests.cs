using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Moq;
using altinnendata_api.Features.Contact;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Admin;
using SjolystInnovation.AppKit.Email;
using Xunit;

namespace altinnendata_api.Tests;

public class ContactSliceTests : TestBase
{
    /// <summary>A sender that hands the owner's message to <paramref name="sent"/> and the receipt to <paramref name="receipt"/>.</summary>
    private static Mock<IEmailSender> Sender(Action<EmailMessage>? sent = null, Action<EmailMessage>? receipt = null)
    {
        var email = new Mock<IEmailSender>();
        var count = 0;
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((m, _) => (count++ == 0 ? sent : receipt)?.Invoke(m))
            .ReturnsAsync(new EmailResult(true));
        return email;
    }

    private static IConfiguration Config(string baseUrl = "https://www.altinnendata.no") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Site:BaseUrl"] = baseUrl })
            .Build();

    [Fact]
    public async Task Send_UsesConfiguredRecipientAndRepliesToSender()
    {
        await using var db = CreateDbContext();
        db.AppSettings.Add(new AppSettings { ContactRecipientEmail = "shop@altinnendata.no" });
        await db.SaveChangesAsync();
        EmailMessage? sent = null;
        var email = Sender(m => sent = m);
        using var cts = new CancellationTokenSource();

        var req = new ContactRequest("Ola", "ola@kunde.no", null, "gaming", "25000+", "gaming-pc", "Hi");
        Assert.IsType<Ok<MessageResponse>>(await SendEnquiry.Handle(req, db, email.Object, Config(), cts.Token));

        email.Verify(e => e.SendAsync(It.IsAny<EmailMessage>(), cts.Token), Times.Exactly(2));
        Assert.Equal("shop@altinnendata.no", sent!.To);
        Assert.Equal("ola@kunde.no", sent.ReplyTo);
        Assert.Equal("New enquiry from Ola", sent.Subject);
    }

    [Fact]
    public async Task Send_FallsBackToDefaultRecipientWhenNoSettings()
    {
        await using var db = CreateDbContext();
        EmailMessage? sent = null;
        var email = Sender(m => sent = m);

        await SendEnquiry.Handle(new ContactRequest("Kari", "kari@kunde.no", null, null, null, null, "Hei"), db, email.Object, Config(), default);

        email.Verify(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Equal("sonyslyst@gmail.com", sent!.To);
        Assert.Equal("kari@kunde.no", sent.ReplyTo);
    }

    [Fact]
    public async Task Send_NamesAndLinksTheBuildTheEnquiryIsAbout()
    {
        await using var db = CreateDbContext();
        db.PcBuilds.Add(new PcBuild
        {
            Slug = "lenovo-rtx-3060-3700x-2",
            Translations = [new PcBuildTranslation { Locale = "no", Title = "Lenovo RTX 3060 / 3700X" }],
        });
        await db.SaveChangesAsync();

        string? body = null;
        var email = Sender(m => body = m.HtmlBody);

        var req = new ContactRequest("Ola", "ola@kunde.no", null, null, null, "lenovo-rtx-3060-3700x-2", "Er den ledig?");
        await SendEnquiry.Handle(req, db, email.Object, Config(), default);

        Assert.Contains("Lenovo RTX 3060 / 3700X", body);
        Assert.Contains("https://www.altinnendata.no/no/builds/lenovo-rtx-3060-3700x-2", body);
        Assert.Contains("lenovo-rtx-3060-3700x-2", body);
    }

    [Fact]
    public async Task Send_LeavesOutTheFieldsTheSenderDidNotFill()
    {
        await using var db = CreateDbContext();
        string? body = null;
        var email = Sender(m => body = m.HtmlBody);

        await SendEnquiry.Handle(
            new ContactRequest("Kari", "kari@kunde.no", null, null, null, null, "Hei"), db, email.Object, Config(), default);

        Assert.DoesNotContain("Use case", body);
        Assert.DoesNotContain("Budget", body);
        Assert.DoesNotContain("Build", body);
        Assert.Contains("Kari", body);
    }

    [Fact]
    public async Task Send_KeepsAnUnknownSlugAsPlainText()
    {
        await using var db = CreateDbContext();
        string? body = null;
        var email = Sender(m => body = m.HtmlBody);

        await SendEnquiry.Handle(
            new ContactRequest("Ola", "ola@kunde.no", null, null, null, "gone", "Hei"), db, email.Object, Config(), default);

        Assert.Contains("gone", body);
        Assert.DoesNotContain("<a href", body);
    }

    [Fact]
    public async Task Send_SendsTheSenderAReceiptWithTheirMessage_InNorwegianByDefault()
    {
        await using var db = CreateDbContext();
        db.AppSettings.Add(new AppSettings { ContactRecipientEmail = "shop@altinnendata.no", PublicEmail = "post@altinnendata.no" });
        await db.SaveChangesAsync();
        EmailMessage? receipt = null;
        var email = Sender(receipt: m => receipt = m);

        await SendEnquiry.Handle(
            new ContactRequest("Kari <b>", "kari@kunde.no", null, null, null, null, "Jeg angrer kjopet.\nOrdre ae-1 <script>"), db, email.Object, Config(), default);

        Assert.Equal("kari@kunde.no", receipt!.To);
        Assert.Equal("post@altinnendata.no", receipt.ReplyTo);
        Assert.Equal("Vi har mottatt meldingen din", receipt.Subject);
        Assert.Contains("<h2>Vi har mottatt meldingen din</h2>", receipt.HtmlBody);
        Assert.Contains("Hei Kari &lt;b&gt;,", receipt.HtmlBody);
        Assert.Contains("Jeg angrer kjopet.<br/>Ordre ae-1 &lt;script&gt;", receipt.HtmlBody);
        Assert.Contains("Meldingen din", receipt.HtmlBody);
        Assert.DoesNotContain("<script>", receipt.HtmlBody);
        Assert.Contains("Ikke registrert i Merverdiavgiftsregisteret", receipt.HtmlBody);
    }

    [Theory]
    [InlineData("en", false)]
    [InlineData("EN", false)]
    [InlineData("no", true)]
    [InlineData("nb-NO", true)]
    [InlineData("de", true)]
    public void Receipt_FollowsTheLocaleOfThePage(string locale, bool norwegian)
    {
        var receipt = SendEnquiry.BuildReceipt(
            new ContactRequest("Ola", "ola@kunde.no", null, null, null, null, "Hello", locale), new AppSettings());

        Assert.Equal(norwegian ? "Vi har mottatt meldingen din" : "We have received your message", receipt.Subject);
        Assert.Contains(norwegian ? "Meldingen din" : "Your message", receipt.HtmlBody);
        Assert.Contains(norwegian ? "Hei Ola," : "Hi Ola,", receipt.HtmlBody);
        Assert.Contains(norwegian ? "Vi svarer så snart vi kan." : "We will reply as soon as we can.", receipt.HtmlBody);
        Assert.Contains(norwegian ? "Ikke registrert i Merverdiavgiftsregisteret" : "Not registered in the VAT Register", receipt.HtmlBody);
    }

    [Fact]
    public void Validator_RefusesALongLocale()
    {
        var validator = new ContactRequestValidator();

        Assert.True(validator.Validate(new ContactRequest("Ola", "ola@kunde.no", null, null, null, null, "Hei", "nb-NO")).IsValid);
        Assert.False(validator.Validate(new ContactRequest("Ola", "ola@kunde.no", null, null, null, null, "Hei", new string('a', 11))).IsValid);
    }
}
