using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using altinnendata_api.Features.Contact;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Admin;
using SjolystInnovation.AppKit.Email;
using Xunit;

namespace altinnendata_api.Tests;

[Collection("postgres")]
public class ContactSliceTests(PostgresDatabase postgres) : TestBase
{
    private static readonly ILogger<ContactRequest> Log = NullLogger<ContactRequest>.Instance;

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

    private static async Task SetSettingsAsync(ApplicationDbContext db, AppSettings settings)
    {
        db.AppSettings.RemoveRange(await db.AppSettings.ToListAsync());
        settings.Id = 1;
        db.AppSettings.Add(settings);
        await db.SaveChangesAsync();
    }

    private static EmailReceiptGate Gate(TimeProvider time) => new(Options.Create(Limits()), time);

    private static EmailReceiptGateOptions Limits()
    {
        var limits = new EmailReceiptGateOptions();
        SendEnquiry.ReceiptLimits(limits);
        return limits;
    }

    private static EmailReceiptGate ReceiptGate() => Gate(TimeProvider.System);

    private static IConfiguration Config(string baseUrl = "https://www.altinnendata.no") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Site:BaseUrl"] = baseUrl })
            .Build();

    [Fact]
    public async Task Send_SavesTheEnquiryWithEveryField()
    {
        await using var db = await postgres.NewContextAsync();
        db.PcBuilds.Add(new PcBuild
        {
            Slug = "lenovo-rtx-3060-3700x-2",
            Translations = [new PcBuildTranslation { Locale = "no", Title = "Lenovo RTX 3060 / 3700X" }],
        });
        await db.SaveChangesAsync();
        var email = Sender();

        var req = new ContactRequest("Ola", " ola@kunde.no ", "12345678", "gaming", "25000+", "lenovo-rtx-3060-3700x-2", "Er den ledig?", "en");
        var ok = Assert.IsType<Ok<MessageResponse>>(await SendEnquiry.Handle(req, db, email.Object, Config(), ReceiptGate(), Log, default));
        Assert.Equal("Thanks. We will be in touch.", ok.Value!.Message);

        var saved = Assert.Single(db.Enquiries.AsNoTracking());
        Assert.Equal("Ola", saved.Name);
        Assert.Equal("ola@kunde.no", saved.Email);
        Assert.Equal("12345678", saved.Phone);
        Assert.Equal("gaming", saved.UseCase);
        Assert.Equal("25000+", saved.BudgetNok);
        Assert.Equal("lenovo-rtx-3060-3700x-2", saved.BuildSlug);
        Assert.Equal("Lenovo RTX 3060 / 3700X", saved.BuildTitle);
        Assert.Equal("en", saved.Locale);
        Assert.Equal("Er den ledig?", saved.Message);
        Assert.Equal(EnquiryStatus.New, saved.Status);
        Assert.NotNull(saved.NoticeSentAt);
        Assert.True(saved.ReceiptSent);
    }

    [Fact]
    public async Task Send_NoticeCarriesNoPersonalData()
    {
        await using var db = await postgres.NewContextAsync();
        await SetSettingsAsync(db, new AppSettings { ContactRecipientEmail = "shop@altinnendata.no" });
        EmailMessage? sent = null;
        var email = Sender(m => sent = m);

        var req = new ContactRequest("Ola Nordmann", "ola@kunde.no", "98765432", null, null, null, "Hemmelig melding");
        await SendEnquiry.Handle(req, db, email.Object, Config(), ReceiptGate(), Log, default);

        email.Verify(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        var id = Assert.Single(db.Enquiries.AsNoTracking()).Id;
        Assert.Equal("shop@altinnendata.no", sent!.To);
        Assert.Equal("Ny henvendelse", sent.Subject);
        Assert.Null(sent.ReplyTo);
        Assert.Contains($"https://www.altinnendata.no/no/admin/enquiries/{id}", sent.HtmlBody);
        Assert.DoesNotContain("Ola", sent.HtmlBody);
        Assert.DoesNotContain("ola@kunde.no", sent.HtmlBody);
        Assert.DoesNotContain("98765432", sent.HtmlBody);
        Assert.DoesNotContain("Hemmelig", sent.HtmlBody);
        Assert.DoesNotContain(SendEnquiry.NoReceiptNote, sent.HtmlBody);
    }

    [Fact]
    public async Task Send_NoticeSaysWhenTheReceiptWasHeldBack()
    {
        await using var db = await postgres.NewContextAsync();
        var messages = new List<EmailMessage>();
        var email = new Mock<IEmailSender>();
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((m, _) => messages.Add(m))
            .ReturnsAsync(new EmailResult(true));
        var gate = ReceiptGate();

        await SendEnquiry.Handle(new ContactRequest("Kari", "kari@kunde.no", null, null, null, null, "1"), db, email.Object, Config(), gate, Log, default);
        messages.Clear();
        await SendEnquiry.Handle(new ContactRequest("Kari", "kari@kunde.no", null, null, null, null, "2"), db, email.Object, Config(), gate, Log, default);

        var notice = Assert.Single(messages);
        Assert.Contains(SendEnquiry.NoReceiptNote, notice.HtmlBody);
        Assert.False(db.Enquiries.AsNoTracking().OrderBy(e => e.Id).Last().ReceiptSent);
    }

    [Fact]
    public async Task Send_KeepsTheEnquiryWhenTheNoticeFails()
    {
        await using var db = await postgres.NewContextAsync();
        var email = new Mock<IEmailSender>();
        email.SetupSequence(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailResult(false))
            .ReturnsAsync(new EmailResult(true));

        var req = new ContactRequest("Kari", "kari@kunde.no", null, null, null, null, "Hei");
        Assert.IsType<Ok<MessageResponse>>(await SendEnquiry.Handle(req, db, email.Object, Config(), ReceiptGate(), Log, default));

        var saved = Assert.Single(db.Enquiries.AsNoTracking());
        Assert.Null(saved.NoticeSentAt);
        Assert.Equal(1, saved.NoticeAttempts);
        Assert.NotNull(saved.NoticeAttemptedAt);
    }

    [Fact]
    public async Task Send_KeepsTheEnquiryWhenTheNoticeThrows()
    {
        await using var db = await postgres.NewContextAsync();
        var email = new Mock<IEmailSender>();
        email.SetupSequence(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("down"))
            .ReturnsAsync(new EmailResult(true));

        var req = new ContactRequest("Kari", "kari@kunde.no", null, null, null, null, "Hei");
        Assert.IsType<Ok<MessageResponse>>(await SendEnquiry.Handle(req, db, email.Object, Config(), ReceiptGate(), Log, default));

        var saved = Assert.Single(db.Enquiries.AsNoTracking());
        Assert.Null(saved.NoticeSentAt);
        Assert.Equal(1, saved.NoticeAttempts);
    }

    [Fact]
    public async Task Send_StillReturnsOkWhenTheReceiptThrows()
    {
        await using var db = await postgres.NewContextAsync();
        var email = new Mock<IEmailSender>();
        email.SetupSequence(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailResult(true))
            .ThrowsAsync(new HttpRequestException("down"));

        var req = new ContactRequest("Kari", "kari@kunde.no", null, null, null, null, "Hei");
        Assert.IsType<Ok<MessageResponse>>(await SendEnquiry.Handle(req, db, email.Object, Config(), ReceiptGate(), Log, default));
        Assert.NotNull(Assert.Single(db.Enquiries.AsNoTracking()).NoticeSentAt);
    }

    [Fact]
    public async Task Send_MarksTheReceiptUnsentWhenItThrows_AndLogsIt()
    {
        await using var db = await postgres.NewContextAsync();
        var email = new Mock<IEmailSender>();
        email.SetupSequence(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailResult(true))
            .ThrowsAsync(new HttpRequestException("down"));
        var log = new ListLogger<ContactRequest>();

        var req = new ContactRequest("Kari", "kari@kunde.no", null, null, null, null, "Hei");
        Assert.IsType<Ok<MessageResponse>>(await SendEnquiry.Handle(req, db, email.Object, Config(), ReceiptGate(), log, default));

        var saved = await db.Enquiries.AsNoTracking().SingleAsync();
        Assert.False(saved.ReceiptSent);
        Assert.NotNull(saved.NoticeSentAt);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("receipt"));
    }

    [Fact]
    public async Task Send_MarksTheReceiptUnsentWhenItIsRefused()
    {
        await using var db = await postgres.NewContextAsync();
        var email = new Mock<IEmailSender>();
        email.SetupSequence(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailResult(true))
            .ReturnsAsync(new EmailResult(false));

        var req = new ContactRequest("Kari", "kari@kunde.no", null, null, null, null, "Hei");
        Assert.IsType<Ok<MessageResponse>>(await SendEnquiry.Handle(req, db, email.Object, Config(), ReceiptGate(), Log, default));

        Assert.False((await db.Enquiries.AsNoTracking().SingleAsync()).ReceiptSent);
    }

    [Fact]
    public async Task Send_LogsAFailedNotice()
    {
        await using var db = await postgres.NewContextAsync();
        var email = new Mock<IEmailSender>();
        email.SetupSequence(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailResult(false))
            .ReturnsAsync(new EmailResult(true));
        var log = new ListLogger<ContactRequest>();

        await SendEnquiry.Handle(new ContactRequest("Kari", "kari@kunde.no", null, null, null, null, "Hei"), db, email.Object, Config(), ReceiptGate(), log, default);

        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("notice"));
    }

    [Fact]
    public async Task Send_SavesTheEnquiryEvenWhenTheRequestWasAborted()
    {
        await using var db = await postgres.NewContextAsync();
        var email = Sender();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var req = new ContactRequest("Kari", "kari@kunde.no", null, null, null, null, "Hei");
        Assert.IsType<Ok<MessageResponse>>(await SendEnquiry.Handle(req, db, email.Object, Config(), ReceiptGate(), Log, cts.Token));

        Assert.Single(await db.Enquiries.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Send_KeepsAnUnknownSlugWithoutATitle()
    {
        await using var db = await postgres.NewContextAsync();
        var email = Sender();

        await SendEnquiry.Handle(new ContactRequest("Ola", "ola@kunde.no", null, null, null, "gone", "Hei"), db, email.Object, Config(), ReceiptGate(), Log, default);

        var saved = await db.Enquiries.AsNoTracking().SingleAsync();
        Assert.Equal("gone", saved.BuildSlug);
        Assert.Null(saved.BuildTitle);
    }

    private static async Task<int> SeedEnquiryAsync(ApplicationDbContext db, Action<Enquiry>? edit = null)
    {
        var enquiry = new Enquiry
        {
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Name = "Kari", Email = "kari@kunde.no", Locale = "no", Message = "Hei", ReceiptSent = true,
        };
        edit?.Invoke(enquiry);
        db.Enquiries.Add(enquiry);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return enquiry.Id;
    }

    [Fact]
    public async Task TrySendNotice_SkipsANoticeAlreadySent()
    {
        await using var db = await postgres.NewContextAsync();
        var id = await SeedEnquiryAsync(db, e => e.NoticeSentAt = DateTime.UtcNow);
        var email = Sender();

        Assert.False(await SendEnquiry.TrySendNoticeAsync(db, email.Object, Config(), id, DateTime.UtcNow, default));

        email.Verify(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(0, (await db.Enquiries.AsNoTracking().SingleAsync()).NoticeAttempts);
    }

    [Fact]
    public async Task TrySendNotice_FromTwoInstancesAtOnce_SendsOnce()
    {
        await using var db = await postgres.NewContextAsync();
        var id = await SeedEnquiryAsync(db);
        await using var other = postgres.NewContextOn(db);
        var email = Sender();

        var results = await Task.WhenAll(
            SendEnquiry.TrySendNoticeAsync(db, email.Object, Config(), id, DateTime.UtcNow, default),
            SendEnquiry.TrySendNoticeAsync(other, email.Object, Config(), id, DateTime.UtcNow, default));

        Assert.Single(results, r => r);
        email.Verify(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
        var saved = await db.Enquiries.AsNoTracking().SingleAsync();
        Assert.Equal(1, saved.NoticeAttempts);
        Assert.NotNull(saved.NoticeSentAt);
    }

    [Fact]
    public async Task TrySendNotice_CountsEachAttemptAndSendsOnALaterTry()
    {
        await using var db = await postgres.NewContextAsync();
        var id = await SeedEnquiryAsync(db);
        var email = new Mock<IEmailSender>();
        email.SetupSequence(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailResult(false))
            .ReturnsAsync(new EmailResult(true));

        var now = DateTime.UtcNow;
        Assert.False(await SendEnquiry.TrySendNoticeAsync(db, email.Object, Config(), id, now, default));
        Assert.False(await SendEnquiry.TrySendNoticeAsync(db, email.Object, Config(), id, now.AddMinutes(1), default));
        Assert.True(await SendEnquiry.TrySendNoticeAsync(db, email.Object, Config(), id, now.AddMinutes(5), default));
        Assert.False(await SendEnquiry.TrySendNoticeAsync(db, email.Object, Config(), id, now.AddMinutes(10), default));

        var saved = await db.Enquiries.AsNoTracking().SingleAsync();
        Assert.Equal(2, saved.NoticeAttempts);
        Assert.NotNull(saved.NoticeSentAt);
    }

    [Fact]
    public async Task Send_FallsBackToDefaultRecipientWhenNoSettings()
    {
        await using var db = await postgres.NewContextAsync();
        EmailMessage? sent = null;
        var email = Sender(m => sent = m);

        await SendEnquiry.Handle(new ContactRequest("Kari", "kari@kunde.no", null, null, null, null, "Hei"), db, email.Object, Config(), ReceiptGate(), Log, default);

        email.Verify(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.Equal("sonyslyst@gmail.com", sent!.To);
    }

    [Fact]
    public async Task Send_SendsTheSenderAFixedReceipt_WithNothingTheyTyped()
    {
        await using var db = await postgres.NewContextAsync();
        await SetSettingsAsync(db, new AppSettings { ContactRecipientEmail = "shop@altinnendata.no", PublicEmail = "post@altinnendata.no" });
        EmailMessage? receipt = null;
        var email = Sender(receipt: m => receipt = m);

        await SendEnquiry.Handle(
            new ContactRequest("Kari Visit evil.example", " kari@kunde.no ", null, "Win a prize", null, null, "Click https://evil.example now"),
            db, email.Object, Config(), ReceiptGate(), Log, default);

        Assert.Equal("kari@kunde.no", receipt!.To);
        Assert.Equal("post@altinnendata.no", receipt.ReplyTo);
        Assert.Equal("Vi har mottatt meldingen din", receipt.Subject);
        Assert.Contains(">Vi har mottatt meldingen din</h1>", receipt.HtmlBody);
        Assert.Contains(">Melding mottatt</span>", receipt.HtmlBody);
        Assert.Contains(">Du f&#229;r denne e-posten fordi du sendte oss en melding.</td></tr>", receipt.HtmlBody);
        Assert.Contains(" og svarer så snart vi kan.", receipt.HtmlBody);
        Assert.DoesNotContain("Kari", receipt.HtmlBody);
        Assert.DoesNotContain("evil", receipt.HtmlBody);
        Assert.DoesNotContain("prize", receipt.HtmlBody);
        Assert.Contains("<br>post@altinnendata.no</div>", receipt.HtmlBody);
    }

    [Fact]
    public async Task Send_SendsOneReceiptPerAddressInTenMinutes_ButEveryEnquiry()
    {
        await using var db = await postgres.NewContextAsync();
        var messages = new List<EmailMessage>();
        var email = new Mock<IEmailSender>();
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((m, _) => messages.Add(m))
            .ReturnsAsync(new EmailResult(true));
        var cache = ReceiptGate();

        await SendEnquiry.Handle(new ContactRequest("Kari", "kari@kunde.no", null, null, null, null, "1"), db, email.Object, Config(), cache, Log, default);
        await SendEnquiry.Handle(new ContactRequest("Kari", "KARI@kunde.no", null, null, null, null, "2"), db, email.Object, Config(), cache, Log, default);
        await SendEnquiry.Handle(new ContactRequest("Ola", "ola@kunde.no", null, null, null, null, "3"), db, email.Object, Config(), cache, Log, default);

        Assert.Equal(3, messages.Count(m => m.To == "sonyslyst@gmail.com"));
        Assert.Equal(["kari@kunde.no", "ola@kunde.no"], messages.Where(m => m.To != "sonyslyst@gmail.com").Select(m => m.To));
        Assert.Equal(TimeSpan.FromMinutes(10), Limits().AddressWindow);
        var owners = messages.Where(m => m.To == "sonyslyst@gmail.com").ToList();
        Assert.Equal([false, true, false], owners.Select(m => m.HtmlBody.Contains(SendEnquiry.NoReceiptNote)));
        Assert.Equal("Ingen kvittering sendt til avsenderen. Bekreft mottak selv hvis meldingen gjelder angrerett.", SendEnquiry.NoReceiptNote);
    }

    [Fact]
    public async Task Send_AtTheGlobalCap_StillSendsTheEnquiryButNoReceipt()
    {
        await using var db = await postgres.NewContextAsync();
        var messages = new List<EmailMessage>();
        var email = new Mock<IEmailSender>();
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailMessage, CancellationToken>((m, _) => messages.Add(m))
            .ReturnsAsync(new EmailResult(true));
        var gate = ReceiptGate();
        for (var i = 0; i < Limits().GlobalLimit; i++) Assert.True(gate.TryAcquire($"sender{i}@kunde.no"));

        await SendEnquiry.Handle(new ContactRequest("Kari", "kari@kunde.no", null, null, null, null, "Hei"), db, email.Object, Config(), gate, Log, default);

        var owner = Assert.Single(messages);
        Assert.Equal("sonyslyst@gmail.com", owner.To);
        Assert.Contains(SendEnquiry.NoReceiptNote, owner.HtmlBody);
    }

    [Fact]
    public void Gate_RefusesASecondSpellingOfTheSameMailbox_UntilTheWindowHasPassed()
    {
        var clock = new ManualClock();
        var gate = Gate(clock);

        Assert.True(gate.TryAcquire("kari@gmail.com"));
        Assert.False(gate.TryAcquire("k.a.r.i+1@googlemail.com"));
        clock.Advance(Limits().AddressWindow - TimeSpan.FromSeconds(1));
        Assert.False(gate.TryAcquire("kari+2@gmail.com"));
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(gate.TryAcquire("kari+3@gmail.com"));
    }

    [Fact]
    public void Gate_AllowsThirtyReceiptsAnHourAcrossAllAddresses()
    {
        var clock = new ManualClock();
        var gate = Gate(clock);

        for (var i = 0; i < 30; i++)
        {
            Assert.True(gate.TryAcquire($"sender{i}@kunde.no"));
            clock.Advance(TimeSpan.FromMinutes(1));
        }
        Assert.False(gate.TryAcquire("late@kunde.no"));

        // The first receipt was sent 30 minutes ago. An hour after it, one more fits.
        clock.Advance(TimeSpan.FromMinutes(29));
        Assert.False(gate.TryAcquire("late@kunde.no"));
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(gate.TryAcquire("late@kunde.no"));
        Assert.False(gate.TryAcquire("later@kunde.no"));
    }

    [Fact]
    public void RateLimit_ContactIsLimitedPerMinuteAndPerHourPerIp()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "altinnendata-api.csproj"))) dir = dir.Parent;
        var config = new ConfigurationBuilder().AddJsonFile(Path.Combine(dir!.FullName, "appsettings.json")).Build();

        var rules = config.GetSection("RateLimiting:Rules").GetChildren()
            .Where(r => r["Endpoint"] == "*:/api/contact")
            .Select(r => (r["Period"], r["Limit"]))
            .ToList();

        Assert.Equal([("1m", "5"), ("1h", "20")], rules);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
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
    public void Validator_RefusesANulCharacterInAnyTextField()
    {
        var validator = new ContactRequestValidator();
        var nul = "a\0b";
        var requests = new[]
        {
            new ContactRequest(nul, "ola@kunde.no", null, null, null, null, "Hei"),
            new ContactRequest("Ola", nul, null, null, null, null, "Hei"),
            new ContactRequest("Ola", "ola@kunde.no", nul, null, null, null, "Hei"),
            new ContactRequest("Ola", "ola@kunde.no", null, nul, null, null, "Hei"),
            new ContactRequest("Ola", "ola@kunde.no", null, null, nul, null, "Hei"),
            new ContactRequest("Ola", "ola@kunde.no", null, null, null, nul, "Hei"),
            new ContactRequest("Ola", "ola@kunde.no", null, null, null, null, nul),
            new ContactRequest("Ola", "ola@kunde.no", null, null, null, null, "Hei", nul),
        };

        Assert.All(requests, request => Assert.False(validator.Validate(request).IsValid));
    }

    [Fact]
    public void Validator_RefusesALongLocale()
    {
        var validator = new ContactRequestValidator();

        Assert.True(validator.Validate(new ContactRequest("Ola", "ola@kunde.no", null, null, null, null, "Hei", "nb-NO")).IsValid);
        Assert.False(validator.Validate(new ContactRequest("Ola", "ola@kunde.no", null, null, null, null, "Hei", new string('a', 11))).IsValid);
    }
}
