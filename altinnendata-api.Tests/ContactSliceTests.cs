using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using altinnendata_api.Features.Contact;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models.Admin;
using altinnendata_api.Services;
using Xunit;

namespace altinnendata_api.Tests;

public class ContactSliceTests : TestBase
{
    [Fact]
    public async Task Send_UsesConfiguredRecipientAndRepliesToSender()
    {
        await using var db = CreateDbContext();
        db.AppSettings.Add(new AppSettings { ContactRecipientEmail = "shop@altinnendata.no" });
        await db.SaveChangesAsync();
        var email = new Mock<IEmailService>();

        var req = new ContactRequest("Ola", "ola@kunde.no", null, "gaming", 20000, "gaming-pc", "Hi");
        Assert.IsType<Ok<MessageResponse>>(await SendEnquiry.Handle(req, db, email.Object, default));

        email.Verify(e => e.SendEmailAsync("shop@altinnendata.no", It.IsAny<string>(), It.IsAny<string>(), "ola@kunde.no"), Times.Once);
    }

    [Fact]
    public async Task Send_FallsBackToDefaultRecipientWhenNoSettings()
    {
        await using var db = CreateDbContext();
        var email = new Mock<IEmailService>();

        await SendEnquiry.Handle(new ContactRequest("Kari", "kari@kunde.no", null, null, null, null, "Hei"), db, email.Object, default);

        email.Verify(e => e.SendEmailAsync("sonyslyst@gmail.com", It.IsAny<string>(), It.IsAny<string>(), "kari@kunde.no"), Times.Once);
    }
}
