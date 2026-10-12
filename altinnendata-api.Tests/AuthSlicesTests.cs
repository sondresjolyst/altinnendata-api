using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Moq;
using altinnendata_api.Features.Auth;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using SjolystInnovation.AppKit.Auth;
using Xunit;

namespace altinnendata_api.Tests;

public class AuthSlicesTests : TestBase
{
    [Fact]
    public async Task Login_UnknownEmail_ReturnsUnauthorized()
    {
        await using var db = CreateDbContext();
        MockUserManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

        var result = await Login.Handle(new LoginModel { Email = "x@y.no", Password = "nope" },
            MockUserManager.Object, MockSignInManager.Object, AuthServices(db).Jwt, AuthServices(db).RefreshTokens, db);

        var json = Assert.IsType<JsonHttpResult<MessageResponse>>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, json.StatusCode);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokens()
    {
        await using var db = CreateDbContext();
        var user = MakeUser();
        MockUserManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
        MockSignInManager.Setup(m => m.PasswordSignInAsync(user.UserName!, It.IsAny<string>(), false, true))
            .ReturnsAsync(SignInResult.Success);
        MockUserManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string> { "Admin" });

        var result = await Login.Handle(new LoginModel { Email = "a@b.no", Password = "Password1" },
            MockUserManager.Object, MockSignInManager.Object, AuthServices(db).Jwt, AuthServices(db).RefreshTokens, db);

        Assert.IsType<Ok<TokenResponse>>(result);
        Assert.Single(db.RefreshTokens);
    }

    /// <summary>Registers a user the refresh slice can find, and returns a usable access token.</summary>
    private (User user, string accessToken) SeedUser(ApplicationDbContext db)
    {
        var user = MakeUser();
        MockUserManager.Setup(m => m.FindByIdAsync(user.Id)).ReturnsAsync(user);
        MockUserManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string> { "Admin" });
        return (user, AuthServices(db).Jwt.Build(user, new List<string> { "Admin" }));
    }

    private static string SeedRefreshToken(
        ApplicationDbContext db, string userId, DateTime? revoked = null, DateTime? expires = null, DateTime? created = null)
    {
        var raw = Guid.NewGuid().ToString("N");
        db.RefreshTokens.Add(new RefreshToken
        {
            Token = VerificationCodes.Hash(raw),
            UserId = userId,
            Created = created ?? DateTime.UtcNow.AddMinutes(-10),
            Expires = expires ?? DateTime.UtcNow.AddMonths(6),
            Revoked = revoked
        });
        db.SaveChanges();
        return raw;
    }

    private Task<IResult> CallRefresh(ApplicationDbContext db, string accessToken, string refreshToken) =>
        Refresh.Handle(
            new RefreshTokenRequestDto { Token = accessToken, RefreshToken = refreshToken },
            MockUserManager.Object, AuthServices(db).Jwt, AuthServices(db).RefreshTokens);

    [Fact]
    public async Task Refresh_IssuedToken_WorksOnTheNextCall()
    {
        await using var db = CreateDbContext();
        var (user, accessToken) = SeedUser(db);
        var raw = SeedRefreshToken(db, user.Id);

        // The sequence the app actually performs: rotate, store the successor, rotate again.
        var first = Assert.IsType<Ok<TokenResponse>>(await CallRefresh(db, accessToken, raw));
        Assert.NotEqual(raw, first.Value!.RefreshToken);

        var second = await CallRefresh(db, first.Value.Token, first.Value.RefreshToken);

        Assert.IsType<Ok<TokenResponse>>(second);
        Assert.Single(db.RefreshTokens.Where(t => t.Revoked == null));
    }

    [Fact]
    public async Task Refresh_AtTheLiveTokenCap_KeepsTheOtherDevicesSignedIn()
    {
        await using var db = CreateDbContext();
        var (user, accessToken) = SeedUser(db);
        var oldest = SeedRefreshToken(db, user.Id, created: DateTime.UtcNow.AddDays(-5));
        for (var i = 4; i >= 2; i--) SeedRefreshToken(db, user.Id, created: DateTime.UtcNow.AddDays(-i));
        var presented = SeedRefreshToken(db, user.Id, created: DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(5, db.RefreshTokens.Count(t => t.Revoked == null));

        var result = await CallRefresh(db, accessToken, presented);

        Assert.IsType<Ok<TokenResponse>>(result);
        // Rotating at the cap consumes the presented token, so the successor fits without
        // touching anyone else: the user's other devices must stay signed in.
        Assert.Null(db.RefreshTokens.Single(t => t.Token == VerificationCodes.Hash(oldest)).Revoked);
        Assert.Equal(5, db.RefreshTokens.Count(t => t.Revoked == null));
        // The consumed token is kept and marked revoked, which is what makes a replay detectable.
        Assert.NotNull(db.RefreshTokens.Single(t => t.Token == VerificationCodes.Hash(presented)).Revoked);
    }

    [Fact]
    public async Task Refresh_OverTheLiveTokenCap_EvictsEveryExcessToken()
    {
        await using var db = CreateDbContext();
        var (user, accessToken) = SeedUser(db);
        for (var i = 8; i >= 2; i--) SeedRefreshToken(db, user.Id, created: DateTime.UtcNow.AddDays(-i));
        var presented = SeedRefreshToken(db, user.Id, created: DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(8, db.RefreshTokens.Count(t => t.Revoked == null));

        var result = await CallRefresh(db, accessToken, presented);

        // Removing one row per issuance never pulled the count back once it had drifted above
        // the cap.
        Assert.IsType<Ok<TokenResponse>>(result);
        Assert.Equal(5, db.RefreshTokens.Count(t => t.Revoked == null));
    }

    [Fact]
    public async Task Refresh_EvictedToken_DoesNotRevokeTheOtherSessions()
    {
        await using var db = CreateDbContext();
        var (user, accessToken) = SeedUser(db);
        var evicted = SeedRefreshToken(db, user.Id, created: DateTime.UtcNow.AddDays(-9));
        for (var i = 8; i >= 2; i--) SeedRefreshToken(db, user.Id, created: DateTime.UtcNow.AddDays(-i));
        var presented = SeedRefreshToken(db, user.Id, created: DateTime.UtcNow.AddMinutes(-1));
        await CallRefresh(db, accessToken, presented);
        Assert.Empty(db.RefreshTokens.Where(t => t.Token == VerificationCodes.Hash(evicted)));

        var result = await CallRefresh(db, accessToken, evicted);

        // An evicted device coming back is not a replay. Keeping its row would make this look
        // like one and sign the user out of every other device.
        var json = Assert.IsType<JsonHttpResult<MessageResponse>>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, json.StatusCode);
        Assert.Equal(5, db.RefreshTokens.Count(t => t.Revoked == null));
    }

    [Fact]
    public async Task Refresh_ReplayedToken_RevokesTheWholeFamily()
    {
        await using var db = CreateDbContext();
        var (user, accessToken) = SeedUser(db);
        var raw = SeedRefreshToken(db, user.Id, revoked: DateTime.UtcNow.AddMinutes(-1));
        SeedRefreshToken(db, user.Id);

        var result = await CallRefresh(db, accessToken, raw);

        // Replay detection is the reason a consumed token is kept rather than deleted, so it
        // must keep working alongside the cap eviction.
        var json = Assert.IsType<JsonHttpResult<MessageResponse>>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, json.StatusCode);
        Assert.Empty(db.RefreshTokens.Where(t => t.Revoked == null));
    }

    [Fact]
    public async Task Refresh_LiveToken_RotatesAndRevokesPresentedToken()
    {
        await using var db = CreateDbContext();
        var (user, accessToken) = SeedUser(db);
        var raw = SeedRefreshToken(db, user.Id);

        var result = await CallRefresh(db, accessToken, raw);

        Assert.IsType<Ok<TokenResponse>>(result);
        Assert.NotNull(db.RefreshTokens.Single(t => t.Token == VerificationCodes.Hash(raw)).Revoked);
        Assert.Single(db.RefreshTokens.Where(t => t.Revoked == null));
    }

    [Fact]
    public async Task Logout_DeletesTheLiveTokenAndKeepsTheOthers()
    {
        await using var db = CreateDbContext();
        var (user, _) = SeedUser(db);
        var raw = SeedRefreshToken(db, user.Id);
        var other = SeedRefreshToken(db, user.Id);

        var result = await Logout.Handle(new LogoutRequestDto(raw), AuthServices(db).RefreshTokens);

        Assert.IsType<NoContent>(result);
        Assert.Equal(VerificationCodes.Hash(other), Assert.Single(db.RefreshTokens).Token);
    }

    [Fact]
    public async Task Logout_UnknownToken_AnswersTheSame()
    {
        await using var db = CreateDbContext();
        var (user, _) = SeedUser(db);
        SeedRefreshToken(db, user.Id);

        var result = await Logout.Handle(new LogoutRequestDto("made-up"), AuthServices(db).RefreshTokens);

        Assert.IsType<NoContent>(result);
        Assert.Single(db.RefreshTokens);
    }

    [Fact]
    public async Task Refresh_DeletedUser_ReturnsUnauthorizedAndKeepsTheToken()
    {
        await using var db = CreateDbContext();
        var (user, accessToken) = SeedUser(db);
        user.IsDeleted = true;
        SeedRefreshToken(db, user.Id);
        var raw = SeedRefreshToken(db, user.Id);

        var result = await CallRefresh(db, accessToken, raw);

        var json = Assert.IsType<JsonHttpResult<MessageResponse>>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, json.StatusCode);
        Assert.Equal(2, db.RefreshTokens.Count(t => t.Revoked == null));
    }

    [Fact]
    public async Task Refresh_ExpiredToken_ReturnsUnauthorized()
    {
        await using var db = CreateDbContext();
        var (user, accessToken) = SeedUser(db);
        var raw = SeedRefreshToken(db, user.Id, expires: DateTime.UtcNow.AddDays(-1));

        var result = await CallRefresh(db, accessToken, raw);

        var json = Assert.IsType<JsonHttpResult<MessageResponse>>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, json.StatusCode);
    }
}
