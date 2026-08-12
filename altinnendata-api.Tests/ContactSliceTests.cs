using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Moq;
using altinnendata_api.Features.Contact;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Admin;
using altinnendata_api.Services;
using Xunit;

namespace altinnendata_api.Tests;

public class ContactSliceTests : TestBase
{
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
        var email = new Mock<IEmailService>();

        var req = new ContactRequest("Ola", "ola@kunde.no", null, "gaming", 20000, "gaming-pc", "Hi");
        Assert.IsType<Ok<MessageResponse>>(await SendEnquiry.Handle(req, db, email.Object, Config(), default));

        email.Verify(e => e.SendEmailAsync("shop@altinnendata.no", It.IsAny<string>(), It.IsAny<string>(), "ola@kunde.no"), Times.Once);
    }

    [Fact]
    public async Task Send_FallsBackToDefaultRecipientWhenNoSettings()
    {
        await using var db = CreateDbContext();
        var email = new Mock<IEmailService>();

        await SendEnquiry.Handle(new ContactRequest("Kari", "kari@kunde.no", null, null, null, null, "Hei"), db, email.Object, Config(), default);

        email.Verify(e => e.SendEmailAsync("sonyslyst@gmail.com", It.IsAny<string>(), It.IsAny<string>(), "kari@kunde.no"), Times.Once);
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

        var email = new Mock<IEmailService>();
        string? body = null;
        email.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string?>((_, _, html, _) => body = html);

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
        var email = new Mock<IEmailService>();
        string? body = null;
        email.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string?>((_, _, html, _) => body = html);

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
        var email = new Mock<IEmailService>();
        string? body = null;
        email.Setup(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<string, string, string, string?>((_, _, html, _) => body = html);

        await SendEnquiry.Handle(
            new ContactRequest("Ola", "ola@kunde.no", null, null, null, "gone", "Hei"), db, email.Object, Config(), default);

        Assert.Contains("gone", body);
        Assert.DoesNotContain("<a href", body);
    }
}
