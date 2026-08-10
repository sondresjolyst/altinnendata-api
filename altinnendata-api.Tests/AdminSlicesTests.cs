using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using altinnendata_api.Features.Admin;
using altinnendata_api.Models;
using altinnendata_api.Services;
using Xunit;

namespace altinnendata_api.Tests;

public class AdminSlicesTests : TestBase
{
    private static PcBuild Build(string slug, bool published) => new() { Slug = slug, Published = published };

    [Fact]
    public async Task GetStats_ReturnsCounts()
    {
        await using var db = CreateDbContext();
        var deleted = MakeUser("u-del", "del@example.com");
        deleted.IsDeleted = true;
        db.Users.AddRange(MakeUser("u1", "a@example.com"), MakeUser("u2", "b@example.com"), deleted);
        db.PcBuilds.AddRange(Build("a", true), Build("b", true), Build("c", false));
        db.ContentImages.Add(new ContentImage { FileName = "x.png", ContentType = "image/png", StoredPath = "x.png" });
        await db.SaveChangesAsync();

        var ok = Assert.IsType<Ok<AdminStatsDto>>(await AdminDashboard.GetStats(db, Configuration, NullLoggerFactory.Instance, default));
        Assert.Equal(2, ok.Value!.TotalUsers);
        Assert.Equal(2, ok.Value.PublishedBuilds);
        Assert.Equal(1, ok.Value.DraftBuilds);
        Assert.Equal(1, ok.Value.ContentImages);
    }

    [Fact]
    public async Task GetStatsHistory_ReturnsTodayRow()
    {
        await using var db = CreateDbContext();
        db.Users.Add(MakeUser("u1", "a@example.com"));
        await db.SaveChangesAsync();

        var ok = Assert.IsType<Ok<List<DailyStatDto>>>(await AdminDashboard.GetStatsHistory(db, default));
        Assert.NotEmpty(ok.Value!);
        Assert.Equal(1, ok.Value!.Last().TotalUsers);
    }

    [Fact]
    public void GetRoles_ReturnsAllRoles()
    {
        var ok = Assert.IsType<Ok<string[]>>(AdminDashboard.GetRoles());
        Assert.Contains("Admin", ok.Value!);
        Assert.Contains("Default", ok.Value!);
    }

    [Fact]
    public async Task GetEmailStats_ReturnsStats()
    {
        var email = new Mock<IEmailService>();
        email.Setup(e => e.GetEmailStatsAsync(30)).ReturnsAsync(new EmailStatsDto { Days = 30, Requests = 5 });
        var ok = Assert.IsType<Ok<EmailStatsDto>>(await AdminDashboard.GetEmailStats(email.Object, NullLoggerFactory.Instance, 30));
        Assert.Equal(5, ok.Value!.Requests);
    }

    [Fact]
    public async Task GetEmailStats_BrevoFailure_Returns502()
    {
        var email = new Mock<IEmailService>();
        email.Setup(e => e.GetEmailStatsAsync(It.IsAny<int>())).ThrowsAsync(new Exception("brevo down"));
        var problem = Assert.IsType<ProblemHttpResult>(await AdminDashboard.GetEmailStats(email.Object, NullLoggerFactory.Instance, 30));
        Assert.Equal(StatusCodes.Status502BadGateway, problem.StatusCode);
    }
}
