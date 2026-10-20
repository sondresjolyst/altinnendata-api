using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Memory;
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

    private static IMemoryCache Cache() => new MemoryCache(new MemoryCacheOptions());

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
        Assert.IsType<Ok<MessageResponse>>(await SendEnquiry.Handle(req, db, email.Object, Config(), Cache(), cts.Token));

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

        await SendEnquiry.Handle(new ContactRequest("Kari", "kari@kunde.no", null, null, null, null, "Hei"), db, email.Object, Config(), Cache(), default);

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
        await SendEnquiry.Handle(req, db, email.Object, Config(), Cache(), default);

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
            new ContactRequest("Kari", "kari@kunde.no", null, null, null, null, "Hei"), db, email.Object, Config(), Cache(), default);

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
            new ContactRequest("Ola", "ola@kunde.no", null, null, null, "gone", "Hei"), db, email.Object, Config(), Cache(), default);

        Assert.Contains("gone", body);
        Assert.DoesNotContain("<a href", body);
    }

    [Fact]
    public async Task Send_SendsTheSenderAFixedReceipt_WithNothingTheyTyped()
    {
        await using var db = CreateDbContext();
        db.AppSettings.Add(new AppSettings { ContactRecipientEmail = "shop@altinnendata.no", PublicEmail = "post@altinnendata.no" });
        await db.SaveChangesAsync();
        EmailMessage? receipt = null;
        var email = Sender(receipt: m => receipt = m);

        await SendEnquiry.Handle(
            new ContactRequest("Kari Visit evil.example", " kari@kunde.no ", null, "Win a prize", null, null, "Click https://evil.example now"),
            db, email.Object, Config(), Cache(), default);

        Assert.Equal("kari@kunde.no", receipt!.To);
        Assert.Equal("post@altinnendata.no", receipt.ReplyTo);
        Assert.Equal("Vi har mottatt meldingen din", receipt.Subject);
        Assert.Contains("<h2>Vi har mottatt meldingen din</h2>", receipt.HtmlBody);
        Assert.Contains(" og svarer så snart vi kan.", receipt.HtmlBody);
        Assert.DoesNotContain("Kari", receipt.HtmlBody);
        Assert.DoesNotContain("evil", receipt.HtmlBody);
        Assert.DoesNotContain("prize", receipt.HtmlBody);
        Assert.Contains("post@altinnendata.no</p>", receipt.HtmlBody);
    }

    [Fact]
    public async Task Send_TheOwnerReplyToIsTheTrimmedAddress()
    {
        await using var db = CreateDbContext();
        EmailMessage? sent = null;
        var email = Sender(m => sent = m);

        await SendEnquiry.Handle(new ContactRequest("Kari", " kari@kunde.no ", null, null, null, null, "Hei"), db, email.Object, Config(), Cache(), default);

        Assert.Equal("kari@kunde.no", sent!.ReplyTo);
    }

    [Fact]
    public async Task Send_SendsOneReceiptPerAddressInTenMinutes_ButEveryEnquiry()
    {
        await using var db = CreateDbContext();
        var messages = new List<EmailMessage>();
        var email = new Mock<IEmailSender>();
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((m, _) => messages.Add(m))
            .ReturnsAsync(new EmailResult(true));
        var cache = Cache();

        await SendEnquiry.Handle(new ContactRequest("Kari", "kari@kunde.no", null, null, null, null, "1"), db, email.Object, Config(), cache, default);
        await SendEnquiry.Handle(new ContactRequest("Kari", "KARI@kunde.no", null, null, null, null, "2"), db, email.Object, Config(), cache, default);
        await SendEnquiry.Handle(new ContactRequest("Ola", "ola@kunde.no", null, null, null, null, "3"), db, email.Object, Config(), cache, default);

        Assert.Equal(3, messages.Count(m => m.To == "sonyslyst@gmail.com"));
        Assert.Equal(["kari@kunde.no", "ola@kunde.no"], messages.Where(m => m.To != "sonyslyst@gmail.com").Select(m => m.To));
        Assert.Equal(TimeSpan.FromMinutes(10), SendEnquiry.ReceiptWindow);
    }

    [Theory]
    [InlineData("en", false)]
    [InlineData("EN", false)]
    [InlineData("no", true)]
    [InlineData("nb-NO", true)]
    [InlineData(null, true)]
    [InlineData("de", true)]
    public void Receipt_FollowsTheLocaleOfThePage(string? locale, bool norwegian)
    {
        var receipt = SendEnquiry.BuildReceipt("ola@kunde.no", locale, new AppSettings { OrgNumber = "938 433 240" }, new DateTime(2026, 10, 19, 12, 5, 0, DateTimeKind.Utc));

        Assert.Equal("ola@kunde.no", receipt.To);
        Assert.Equal(norwegian ? "Vi har mottatt meldingen din" : "We have received your message", receipt.Subject);
        Assert.Contains(norwegian
            ? "<p>Vi har mottatt meldingen din 19.10.2026 kl. 14:05 og svarer så snart vi kan.</p>"
            : "<p>We received your message on 19 October 2026 at 14:05 and will reply as soon as we can.</p>", receipt.HtmlBody);
        Assert.Contains(norwegian ? "Org.nr." : "Org. no.", receipt.HtmlBody);
        Assert.Contains("Foretaksregisteret", receipt.HtmlBody);
    }

    [Theory]
    [InlineData("kari@kunde.no", true)]
    [InlineData(" kari@kunde.no ", true)]
    [InlineData("kari@kunde.no,ola@kunde.no", false)]
    [InlineData("kari@kunde.no;ola@kunde.no", false)]
    [InlineData("Kari <kari@kunde.no>", false)]
    [InlineData("<kari@kunde.no>", false)]
    [InlineData("kari@kunde.no>", false)]
    [InlineData("kari@kunde.no\r\nBcc: ola@kunde.no", false)]
    [InlineData("kari@kunde.no\n", false)]
    [InlineData("\"Kari\" kari@kunde.no", false)]
    [InlineData("kari", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSingleAddress_TakesOnePlainAddressOnly(string? value, bool valid) =>
        Assert.Equal(valid, ContactRequestValidator.IsSingleAddress(value));

    [Fact]
    public void Validator_RefusesAListOfAddresses_UnderTheEmailField()
    {
        var result = new ContactRequestValidator().Validate(
            new ContactRequest("Ola", "ola@kunde.no, kari@kunde.no", null, null, null, null, "Hei"));

        Assert.NotEmpty(result.Errors);
        Assert.All(result.Errors, e => Assert.Equal("email", e.PropertyName));
        Assert.Contains(result.Errors, e => e.ErrorMessage == "'Email' is not a valid email address.");
    }

    [Fact]
    public void Validator_RefusesALongLocale()
    {
        var validator = new ContactRequestValidator();

        Assert.True(validator.Validate(new ContactRequest("Ola", "ola@kunde.no", null, null, null, null, "Hei", "nb-NO")).IsValid);
        Assert.False(validator.Validate(new ContactRequest("Ola", "ola@kunde.no", null, null, null, null, "Hei", new string('a', 11))).IsValid);
    }
}
