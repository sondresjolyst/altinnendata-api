using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using altinnendata_api.Features.Admin;
using altinnendata_api.Models;
using altinnendata_api.Models.Admin;
using SjolystInnovation.AppKit.AdminStats;
using SjolystInnovation.AppKit.Storage.Images;
using altinnendata_api.Services;
using Xunit;

namespace altinnendata_api.Tests;

public class AdminSlicesTests : TestBase
{
    private static PcBuild Build(string slug, bool published) => new() { Slug = slug, Published = published };

    private static AppKitDailyStats<ApplicationDbContext, DailyStatSnapshot> Stats() =>
        new(Options.Create(new AppKitDailyStatsOptions<ApplicationDbContext, DailyStatSnapshot> { LiveSampler = AdminDashboard.SampleAsync }), TimeProvider.System);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

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

        var ok = Assert.IsType<Ok<AdminStatsDto>>(await AdminDashboard.GetStats(db, Options.Create(new AppKitContentImagesOptions()), NullLoggerFactory.Instance, default));
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

        var ok = Assert.IsType<Ok<List<DailyStatDto>>>(await AdminDashboard.GetStatsHistory(db, Stats(), default));
        Assert.NotEmpty(ok.Value!);
        Assert.Equal(1, ok.Value!.Last().TotalUsers);
        Assert.Equal(Today.ToString("yyyy-MM-dd"), ok.Value!.Last().Date);
    }

    [Fact]
    public async Task GetStatsHistory_ReturnsStoredDaysAndDerivesSoldBuildsFromTheSaleDate()
    {
        await using var db = CreateDbContext();
        db.DailyStatSnapshots.AddRange(
            new DailyStatSnapshot { Date = Today.AddDays(-3), PublishedBuilds = 5 },
            new DailyStatSnapshot { Date = Today.AddDays(-1), PublishedBuilds = 7 });
        db.PcBuilds.AddRange(
            new PcBuild { Slug = "a", Availability = BuildAvailability.Sold, SoldOn = Today.AddDays(-2) },
            new PcBuild { Slug = "b", Availability = BuildAvailability.Sold, SoldOn = Today },
            new PcBuild { Slug = "c", Availability = BuildAvailability.Sold });
        await db.SaveChangesAsync();

        var ok = Assert.IsType<Ok<List<DailyStatDto>>>(await AdminDashboard.GetStatsHistory(db, Stats(), default));

        Assert.Equal([Today.AddDays(-3), Today.AddDays(-1), Today], ok.Value!.Select(d => DateOnly.Parse(d.Date)));
        Assert.Equal([0, 1, 2], ok.Value!.Select(d => d.SoldBuilds));
        Assert.Equal(7, ok.Value![1].PublishedBuilds);
    }

    [Fact]
    public async Task GetStatsHistory_StartsAtFrom()
    {
        await using var db = CreateDbContext();
        db.DailyStatSnapshots.AddRange(
            new DailyStatSnapshot { Date = Today.AddDays(-3), TotalUsers = 1 },
            new DailyStatSnapshot { Date = Today.AddDays(-1), TotalUsers = 2 });
        await db.SaveChangesAsync();

        var ok = Assert.IsType<Ok<List<DailyStatDto>>>(await AdminDashboard.GetStatsHistory(db, Stats(), default, Today.AddDays(-2)));

        Assert.Equal([Today.AddDays(-1), Today], ok.Value!.Select(d => DateOnly.Parse(d.Date)));
    }

    [Fact]
    public async Task GetStatsHistory_DoesNotStoreToday()
    {
        await using var db = CreateDbContext();
        db.Users.Add(MakeUser("u1", "a@example.com"));
        await db.SaveChangesAsync();

        await AdminDashboard.GetStatsHistory(db, Stats(), default);

        Assert.Empty(db.DailyStatSnapshots);
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
