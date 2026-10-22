using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using altinnendata_api.Constants;
using altinnendata_api.Features.Contact;
using altinnendata_api.Models;
using Xunit;

namespace altinnendata_api.Tests;

[Collection("postgres")]
public class AdminEnquiriesTests(PostgresDatabase postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTime Base = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static async Task<User> AddAdminAsync(ApplicationDbContext db, string id, string first, string last = "Admin", bool deleted = false, bool admin = true)
    {
        var user = new User
        {
            Id = id, Email = $"{id}@example.com", UserName = id, NormalizedUserName = id.ToUpperInvariant(),
            FirstName = first, LastName = last, EmailConfirmed = true, IsDeleted = deleted,
        };
        db.Users.Add(user);
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Name == RoleNames.Admin, Ct);
        if (role == null)
        {
            role = new IdentityRole { Id = "role-admin", Name = RoleNames.Admin, NormalizedName = RoleNames.Admin.ToUpperInvariant() };
            db.Roles.Add(role);
        }
        if (admin) db.UserRoles.Add(new IdentityUserRole<string> { UserId = id, RoleId = role.Id });
        await db.SaveChangesAsync(Ct);
        return user;
    }

    private static async Task<Enquiry> AddEnquiryAsync(ApplicationDbContext db, int daysOld = 0, EnquiryStatus status = EnquiryStatus.New,
        string name = "Ola", string email = "ola@kunde.no", string? owner = null, bool ownerRemoved = false)
    {
        var enquiry = new Enquiry
        {
            CreatedAt = Base.AddDays(-daysOld), UpdatedAt = Base.AddDays(-daysOld), Name = name, Email = email,
            Locale = "no", Message = "Hei", Status = status, AssignedUserId = owner, OwnerRemoved = ownerRemoved,
        };
        db.Enquiries.Add(enquiry);
        await db.SaveChangesAsync(Ct);
        return enquiry;
    }

    private static HttpContext As(string userId) => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test")),
    };

    private static EnquiryPage Page(IResult result) => Assert.IsType<Ok<EnquiryPage>>(result).Value!;

    [Fact]
    public async Task List_HidesDoneByDefault_NewestFirst()
    {
        await using var db = await postgres.NewContextAsync();
        var old = await AddEnquiryAsync(db, 5);
        var recent = await AddEnquiryAsync(db, 1, EnquiryStatus.Answered);
        await AddEnquiryAsync(db, 0, EnquiryStatus.Done);

        var page = Page(await AdminEnquiries.List(db, As("x"), null, false, null, null, Ct));

        Assert.Equal(2, page.Total);
        Assert.Equal([recent.Id, old.Id], page.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task List_FiltersByStatusMineAndSearch()
    {
        await using var db = await postgres.NewContextAsync();
        var me = await AddAdminAsync(db, "me", "Mia");
        var other = await AddAdminAsync(db, "other", "Per");
        var kari = await AddEnquiryAsync(db, 3, name: "Kari Nordmann", email: "Kari@Firma.no", owner: me.Id);
        var done = await AddEnquiryAsync(db, 2, EnquiryStatus.Done, name: "Ferdig", owner: me.Id);
        await AddEnquiryAsync(db, 1, owner: other.Id);

        Assert.Equal([done.Id], Page(await AdminEnquiries.List(db, As("me"), [EnquiryStatus.Done], false, null, null, Ct)).Items.Select(i => i.Id));
        Assert.Equal([kari.Id], Page(await AdminEnquiries.List(db, As("me"), null, false, "KARI@", null, Ct)).Items.Select(i => i.Id));
        Assert.Equal([kari.Id], Page(await AdminEnquiries.List(db, As("me"), null, false, "nordmann", null, Ct)).Items.Select(i => i.Id));
        var mine = Page(await AdminEnquiries.List(db, As("me"), null, true, null, null, Ct));
        Assert.Equal([kari.Id], mine.Items.Select(i => i.Id));
        Assert.Equal("Mia Admin", mine.Items[0].Owner!.Name);
    }

    [Fact]
    public async Task List_PagesFiftyAtATime()
    {
        await using var db = await postgres.NewContextAsync();
        for (var i = 0; i < 52; i++) db.Enquiries.Add(new Enquiry { CreatedAt = Base.AddMinutes(i), UpdatedAt = Base, Name = "N", Email = "n@x.no", Locale = "no", Message = "m" });
        await db.SaveChangesAsync(Ct);

        var second = Page(await AdminEnquiries.List(db, As("x"), null, false, null, 2, Ct));

        Assert.Equal(52, second.Total);
        Assert.Equal(2, second.Items.Count);
    }

    [Fact]
    public async Task Count_CountsOnlyNew()
    {
        await using var db = await postgres.NewContextAsync();
        await AddEnquiryAsync(db);
        await AddEnquiryAsync(db);
        await AddEnquiryAsync(db, status: EnquiryStatus.InProgress);
        await AddEnquiryAsync(db, status: EnquiryStatus.Done);

        Assert.Equal(2, Assert.IsType<Ok<EnquiryCountDto>>(await AdminEnquiries.Count(db, Ct)).Value!.New);
    }

    [Fact]
    public async Task Assignees_ListsOnlyActiveAdmins()
    {
        await using var db = await postgres.NewContextAsync();
        await AddAdminAsync(db, "b", "Berit", "Hansen");
        await AddAdminAsync(db, "a", "Anna", "Hansen");
        await AddAdminAsync(db, "gone", "Slettet", deleted: true);
        await AddAdminAsync(db, "plain", "Vanlig", admin: false);

        var list = Assert.IsType<Ok<List<AssigneeDto>>>(await AdminEnquiries.Assignees(db, Ct)).Value!;

        Assert.Equal(["Anna Hansen", "Berit Hansen"], list.Select(a => a.Name));
    }

    [Fact]
    public async Task Get_ShowsARemovedOwnerAsNone()
    {
        await using var db = await postgres.NewContextAsync();
        var owner = await AddAdminAsync(db, "gone", "Slettet");
        var enquiry = await AddEnquiryAsync(db, owner: owner.Id);
        owner.IsDeleted = true;
        await db.SaveChangesAsync(Ct);

        var dto = Assert.IsType<Ok<EnquiryDto>>(await AdminEnquiries.Get(enquiry.Id, db, Ct)).Value!;
        var summary = Page(await AdminEnquiries.List(db, As("x"), null, false, null, null, Ct)).Items.Single();

        Assert.Null(dto.Owner);
        Assert.True(dto.OwnerRemoved);
        Assert.Null(summary.Owner);
        Assert.True(summary.OwnerRemoved);
    }

    [Fact]
    public async Task Get_Returns404ForAnUnknownId()
    {
        await using var db = await postgres.NewContextAsync();

        Assert.IsType<NotFound>(await AdminEnquiries.Get(99, db, Ct));
    }

    [Fact]
    public async Task Patch_TakesAnUnownedCase()
    {
        await using var db = await postgres.NewContextAsync();
        var me = await AddAdminAsync(db, "me", "Mia");
        var enquiry = await AddEnquiryAsync(db);

        var result = await AdminEnquiries.Patch(enquiry.Id, new(EnquiryStatus.InProgress, true, me.Id, null), db, Ct);

        var dto = Assert.IsType<Ok<EnquiryDto>>(result).Value!;
        Assert.Equal(me.Id, dto.Owner!.Id);
        Assert.Equal(EnquiryStatus.InProgress, dto.Status);
        Assert.True(dto.UpdatedAt > enquiry.UpdatedAt);
    }

    [Fact]
    public async Task Patch_TakeOverNeedsTheOwnerTheClientSaw()
    {
        await using var db = await postgres.NewContextAsync();
        var first = await AddAdminAsync(db, "first", "Eva");
        var second = await AddAdminAsync(db, "second", "Per");
        var third = await AddAdminAsync(db, "third", "Tor");
        var enquiry = await AddEnquiryAsync(db, owner: second.Id);

        var stale = await AdminEnquiries.Patch(enquiry.Id, new(EnquiryStatus.Done, true, first.Id, null), db, Ct);

        var conflict = Assert.IsType<Conflict<EnquiryDto>>(stale);
        Assert.Equal(second.Id, conflict.Value!.Owner!.Id);
        var stored = await db.Enquiries.AsNoTracking().SingleAsync(e => e.Id == enquiry.Id, Ct);
        Assert.Equal(second.Id, stored.AssignedUserId);
        Assert.Equal(EnquiryStatus.New, stored.Status);
        Assert.Equal(enquiry.UpdatedAt, stored.UpdatedAt);

        var taken = await AdminEnquiries.Patch(enquiry.Id, new(null, true, third.Id, second.Id), db, Ct);
        Assert.Equal(third.Id, Assert.IsType<Ok<EnquiryDto>>(taken).Value!.Owner!.Id);
    }

    [Fact]
    public async Task Patch_TwoTakeOversOfTheSameCase_OnlyOneWins()
    {
        await using var db = await postgres.NewContextAsync();
        await using var other = postgres.NewContextOn(db);
        var a = await AddAdminAsync(db, "a", "Anna");
        var b = await AddAdminAsync(db, "b", "Bjorn");
        var enquiry = await AddEnquiryAsync(db);

        var results = await Task.WhenAll(
            AdminEnquiries.Patch(enquiry.Id, new(null, true, a.Id, null), db, Ct),
            AdminEnquiries.Patch(enquiry.Id, new(null, true, b.Id, null), other, Ct));

        Assert.Equal(1, results.Count(r => r is Ok<EnquiryDto>));
        Assert.Equal(1, results.Count(r => r is Conflict<EnquiryDto>));
    }

    [Fact]
    public async Task Patch_RejectsAnOwnerWhoIsNotAnActiveAdmin()
    {
        await using var db = await postgres.NewContextAsync();
        var plain = await AddAdminAsync(db, "plain", "Vanlig", admin: false);
        await AddAdminAsync(db, "gone", "Slettet", deleted: true);
        var enquiry = await AddEnquiryAsync(db);

        foreach (var id in new[] { plain.Id, "gone", "nobody" })
            Assert.IsType<ValidationProblem>(await AdminEnquiries.Patch(enquiry.Id, new(null, true, id, null), db, Ct));

        Assert.Null((await db.Enquiries.AsNoTracking().SingleAsync(e => e.Id == enquiry.Id, Ct)).AssignedUserId);
    }

    [Fact]
    public async Task Patch_ChangesStatusWithoutTouchingTheOwner()
    {
        await using var db = await postgres.NewContextAsync();
        var owner = await AddAdminAsync(db, "owner", "Eva");
        var enquiry = await AddEnquiryAsync(db, owner: owner.Id);

        var result = await AdminEnquiries.Patch(enquiry.Id, new(EnquiryStatus.Answered, false, null, "stale"), db, Ct);

        var dto = Assert.IsType<Ok<EnquiryDto>>(result).Value!;
        Assert.Equal(EnquiryStatus.Answered, dto.Status);
        Assert.Equal(owner.Id, dto.Owner!.Id);
    }

    [Fact]
    public async Task Patch_TakingTheCaseClearsOwnerRemoved()
    {
        await using var db = await postgres.NewContextAsync();
        var me = await AddAdminAsync(db, "me", "Mia");
        var enquiry = await AddEnquiryAsync(db, ownerRemoved: true);

        var dto = Assert.IsType<Ok<EnquiryDto>>(await AdminEnquiries.Patch(enquiry.Id, new(null, true, me.Id, null), db, Ct)).Value!;

        Assert.False(dto.OwnerRemoved);
        Assert.False((await db.Enquiries.AsNoTracking().SingleAsync(e => e.Id == enquiry.Id, Ct)).OwnerRemoved);
    }

    [Fact]
    public async Task Patch_TakesACaseWhoseOwnerWasRemoved()
    {
        await using var db = await postgres.NewContextAsync();
        var gone = await AddAdminAsync(db, "gone", "Slettet", deleted: true);
        var me = await AddAdminAsync(db, "me", "Mia");
        var enquiry = await AddEnquiryAsync(db, owner: gone.Id);

        var dto = Assert.IsType<Ok<EnquiryDto>>(await AdminEnquiries.Patch(enquiry.Id, new(null, true, me.Id, null), db, Ct)).Value!;

        Assert.Equal(me.Id, dto.Owner!.Id);
        Assert.False(dto.OwnerRemoved);
    }

    [Fact]
    public async Task Patch_Returns404ForAnUnknownId()
    {
        await using var db = await postgres.NewContextAsync();

        Assert.IsType<NotFound>(await AdminEnquiries.Patch(99, new(EnquiryStatus.Done, false, null, null), db, Ct));
    }

    [Fact]
    public async Task Delete_RemovesTheEnquiry()
    {
        await using var db = await postgres.NewContextAsync();
        var enquiry = await AddEnquiryAsync(db);

        Assert.IsType<NoContent>(await AdminEnquiries.Delete(enquiry.Id, db, Ct));
        Assert.False(await db.Enquiries.AnyAsync(e => e.Id == enquiry.Id, Ct));
        Assert.IsType<NotFound>(await AdminEnquiries.Delete(enquiry.Id, db, Ct));
    }

    [Fact]
    public void Endpoints_RequireAdmin()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql("Host=localhost"));
        var app = builder.Build();
        new AdminEnquiries.Endpoints().Map(app);

        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).ToList();

        Assert.Equal(6, endpoints.Count);
        Assert.All(endpoints, e =>
            Assert.Contains(e.Metadata.GetOrderedMetadata<IAuthorizeData>(), a => a.Policy == Policies.Admin));
    }

    private sealed class TestAuth(Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-User", out var user)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }

    private async Task<(WebApplication App, HttpClient Client, ApplicationDbContext Db)> StartAsync()
    {
        var db = await postgres.NewContextAsync();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(db.Database.GetConnectionString()));
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuth>("Test", null);
        builder.Services.AddAuthorization(o => o.AddPolicy(Policies.Admin, p => p.RequireAuthenticatedUser()));
        builder.Services.AddValidatorsFromAssemblyContaining<Program>();
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        new AdminEnquiries.Endpoints().Map(app);
        await app.StartAsync(Ct);
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-User", "me");
        return (app, client, db);
    }

    [Fact]
    public async Task Http_BindsTheQueryAndWritesStatusAsAName()
    {
        var (app, client, db) = await StartAsync();
        await using var _ = app;
        await using var __ = db;
        var me = await AddAdminAsync(db, "me", "Mia");
        var kari = await AddEnquiryAsync(db, 3, name: "Kari", email: "kari@firma.no", owner: me.Id);
        var done = await AddEnquiryAsync(db, 2, EnquiryStatus.Done, name: "Ferdig");
        var answered = await AddEnquiryAsync(db, 1, EnquiryStatus.Answered, name: "Svart");

        var plain = await client.GetStringAsync("/api/admin/enquiries", Ct);
        Assert.Contains("\"status\":\"New\"", plain);
        var assignees = await client.GetFromJsonAsync<List<AssigneeDto>>("/api/admin/enquiries/assignees", Ct);
        Assert.Equal(["Mia Admin"], assignees!.Select(a => a.Name));
        Assert.DoesNotContain("Ferdig", plain);

        async Task<int[]> Ids(string query) =>
            (await client.GetFromJsonAsync<EnquiryPage>("/api/admin/enquiries" + query, Ct))!.Items.Select(i => i.Id).ToArray();

        Assert.Equal([answered.Id, done.Id], await Ids("?status=Done&status=Answered"));
        Assert.Equal([kari.Id], await Ids("?mine=true"));
        Assert.Equal([kari.Id], await Ids("?q=KARI@"));
        Assert.Empty(await Ids("?page=2"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.GetTestServer().CreateClient().GetAsync("/api/admin/enquiries", Ct)).StatusCode);

        var patch = await client.PatchAsJsonAsync($"/api/admin/enquiries/{kari.Id}", new { status = "Answered" }, Ct);
        Assert.Contains("\"status\":\"Answered\"", await patch.Content.ReadAsStringAsync(Ct));
        var empty = await client.PatchAsJsonAsync($"/api/admin/enquiries/{kari.Id}", new { }, Ct);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }
}
