using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using altinnendata_api.Constants;
using altinnendata_api.Features.Contact;
using altinnendata_api.Models;
using SjolystInnovation.AppKit.Email;
using Xunit;

namespace altinnendata_api.Tests;

[Collection("postgres")]
public class EnquiryJobsTests(PostgresDatabase postgres)
{
    private static readonly DateTime Now = new(2026, 10, 19, 3, 0, 0, DateTimeKind.Utc);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IConfiguration Config() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Site:BaseUrl"] = "https://www.altinnendata.no" })
            .Build();

    private static Enquiry NewEnquiry(DateTime createdAt, EnquiryStatus status = EnquiryStatus.New) => new()
    {
        CreatedAt = createdAt, UpdatedAt = createdAt, Name = "Ola", Email = "ola@kunde.no",
        Locale = "no", Message = "Hei", Status = status,
    };

    private static Mock<IEmailSender> Sender()
    {
        var email = new Mock<IEmailSender>();
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(new EmailResult(true));
        return email;
    }

    private static async Task<Enquiry> AddUnsentAsync(ApplicationDbContext db, int attempts, DateTime? attemptedAt)
    {
        var enquiry = NewEnquiry(Now.AddDays(-1));
        enquiry.NoticeAttempts = attempts;
        enquiry.NoticeAttemptedAt = attemptedAt;
        db.Enquiries.Add(enquiry);
        await db.SaveChangesAsync(Ct);
        return enquiry;
    }

    [Fact]
    public async Task Retry_SendsAMissedNoticeOnceDue()
    {
        await using var db = await postgres.NewContextAsync();
        var enquiry = await AddUnsentAsync(db, 1, Now.AddMinutes(-6));
        var email = Sender();

        Assert.Equal(1, await EnquiryNoticeRetry.ResendMissingAsync(db, email.Object, Config(), Now, Ct));

        Assert.Equal(Now, (await db.Enquiries.AsNoTracking().SingleAsync(e => e.Id == enquiry.Id, Ct)).NoticeSentAt);
        email.Verify(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Retry_WaitsForTheBackoff()
    {
        await using var db = await postgres.NewContextAsync();
        var enquiry = await AddUnsentAsync(db, 1, Now.AddMinutes(-4));
        var email = Sender();

        Assert.Equal(0, await EnquiryNoticeRetry.ResendMissingAsync(db, email.Object, Config(), Now, Ct));

        Assert.Null((await db.Enquiries.AsNoTracking().SingleAsync(e => e.Id == enquiry.Id, Ct)).NoticeSentAt);
        email.Verify(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Backoff_IsCappedAtAnHour()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), EnquiryNoticeRetry.Backoff(1));
        Assert.Equal(TimeSpan.FromMinutes(10), EnquiryNoticeRetry.Backoff(2));
        Assert.Equal(TimeSpan.FromMinutes(60), EnquiryNoticeRetry.Backoff(10));
        Assert.Equal(TimeSpan.FromMinutes(60), EnquiryNoticeRetry.Backoff(1000));
    }

    [Fact]
    public async Task Retry_SkipsSentNotices()
    {
        await using var db = await postgres.NewContextAsync();
        var enquiry = NewEnquiry(Now.AddDays(-1));
        enquiry.NoticeSentAt = Now.AddHours(-20);
        enquiry.NoticeAttempts = 1;
        enquiry.NoticeAttemptedAt = Now.AddHours(-20);
        db.Enquiries.Add(enquiry);
        await db.SaveChangesAsync(Ct);
        var email = Sender();

        Assert.Equal(0, await EnquiryNoticeRetry.ResendMissingAsync(db, email.Object, Config(), Now, Ct));

        email.Verify(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Retry_SendsAnEnquiryThatWasNeverTried()
    {
        await using var db = await postgres.NewContextAsync();
        await AddUnsentAsync(db, 0, null);

        Assert.Equal(1, await EnquiryNoticeRetry.ResendMissingAsync(db, Sender().Object, Config(), Now, Ct));
    }

    [Fact]
    public async Task Retry_LogsAWarningWithTheIdAndAttempt_WhenTheSendIsRefused()
    {
        await using var db = await postgres.NewContextAsync();
        var enquiry = await AddUnsentAsync(db, 2, Now.AddHours(-1));
        var email = new Mock<IEmailSender>();
        email.Setup(e => e.SendAsync(It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>())).ReturnsAsync(new EmailResult(false, "refused"));
        var log = new ListLogger<EnquiryJobsTests>();

        Assert.Equal(0, await EnquiryNoticeRetry.ResendMissingAsync(db, email.Object, Config(), Now, Ct, log));

        var entry = Assert.Single(log.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, entry.Level);
        Assert.Equal($"The notice of enquiry {enquiry.Id} was not sent on retry 3", entry.Message);
    }

    [Fact]
    public async Task Retry_LogsADatabaseFailureOnOneEnquiry_AndSendsTheNext()
    {
        await using var db = await postgres.NewContextAsync();
        var broken = await AddUnsentAsync(db, 0, null);
        var fine = await AddUnsentAsync(db, 0, null);
        // A trigger makes any update of the first enquiry fail in the database, the way a real outage would.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_claim() RETURNS trigger AS $$
            BEGIN
                IF OLD."Id" = (SELECT MIN("Id") FROM "Enquiries") THEN RAISE EXCEPTION 'claim refused'; END IF;
                RETURN NEW;
            END $$ LANGUAGE plpgsql;
            CREATE TRIGGER fail_claim BEFORE UPDATE ON "Enquiries" FOR EACH ROW EXECUTE FUNCTION fail_claim();
            """, Ct);
        var log = new ListLogger<EnquiryJobsTests>();

        Assert.Equal(1, await EnquiryNoticeRetry.ResendMissingAsync(db, Sender().Object, Config(), Now, Ct, log));

        var saved = await db.Enquiries.AsNoTracking().ToDictionaryAsync(e => e.Id, Ct);
        Assert.Null(saved[broken.Id].NoticeSentAt);
        Assert.NotNull(saved[fine.Id].NoticeSentAt);
        var entry = Assert.Single(log.Entries);
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Error, entry.Level);
    }

    [Fact]
    public async Task Purge_DeletesEnquiriesOlderThanTwelveMonths_InAnyStatus()
    {
        await using var db = await postgres.NewContextAsync();
        var old = Enum.GetValues<EnquiryStatus>().Select(s => NewEnquiry(Now.AddDays(-366), s)).ToList();
        var recent = NewEnquiry(Now.AddDays(-364));
        db.Enquiries.AddRange(old);
        db.Enquiries.Add(recent);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(old.Count, await EnquiryRetention.PurgeAsync(db, Now, Ct));

        Assert.Equal([recent.Id], await db.Enquiries.Select(e => e.Id).ToListAsync(Ct));
    }

    [Fact]
    public async Task Purge_DeletesOneBatchPerRun()
    {
        await using var db = await postgres.NewContextAsync();
        db.Enquiries.AddRange(Enumerable.Range(0, EnquiryRetention.BatchSize + 2).Select(_ => NewEnquiry(Now.AddDays(-400))));
        await db.SaveChangesAsync(Ct);

        Assert.Equal(EnquiryRetention.BatchSize, await EnquiryRetention.PurgeAsync(db, Now, Ct));
        Assert.Equal(2, await EnquiryRetention.PurgeAsync(db, Now, Ct));
    }

    [Fact]
    public async Task ReleaseRemovedOwners_ClearsDeletedAndDemotedOwners()
    {
        await using var db = await postgres.NewContextAsync();
        var adminRole = new IdentityRole { Id = "admin-role", Name = RoleNames.Admin, NormalizedName = RoleNames.Admin.ToUpperInvariant() };
        var otherRole = new IdentityRole { Id = "default-role", Name = RoleNames.Default, NormalizedName = RoleNames.Default.ToUpperInvariant() };
        var active = NewUser("active");
        var deleted = NewUser("deleted");
        deleted.IsDeleted = true;
        var demoted = NewUser("demoted");
        db.Roles.AddRange(adminRole, otherRole);
        db.Users.AddRange(active, deleted, demoted);
        db.UserRoles.AddRange(
            new IdentityUserRole<string> { UserId = active.Id, RoleId = adminRole.Id },
            new IdentityUserRole<string> { UserId = deleted.Id, RoleId = adminRole.Id },
            new IdentityUserRole<string> { UserId = demoted.Id, RoleId = otherRole.Id });
        var kept = NewEnquiry(Now);
        kept.AssignedUserId = active.Id;
        var fromDeleted = NewEnquiry(Now);
        fromDeleted.AssignedUserId = deleted.Id;
        var fromDemoted = NewEnquiry(Now);
        fromDemoted.AssignedUserId = demoted.Id;
        var unassigned = NewEnquiry(Now);
        db.Enquiries.AddRange(kept, fromDeleted, fromDemoted, unassigned);
        await db.SaveChangesAsync(Ct);

        Assert.Equal(2, await EnquiryRetention.ReleaseRemovedOwnersAsync(db, Ct));

        var saved = await db.Enquiries.AsNoTracking().ToDictionaryAsync(e => e.Id, Ct);
        Assert.Equal(active.Id, saved[kept.Id].AssignedUserId);
        Assert.False(saved[kept.Id].OwnerRemoved);
        foreach (var id in new[] { fromDeleted.Id, fromDemoted.Id })
        {
            Assert.Null(saved[id].AssignedUserId);
            Assert.True(saved[id].OwnerRemoved);
        }
        Assert.False(saved[unassigned.Id].OwnerRemoved);
    }

    private static User NewUser(string id) => new()
    {
        Id = id, UserName = id, NormalizedUserName = id.ToUpperInvariant(), Email = $"{id}@x.no",
        FirstName = id, LastName = id,
    };
}
