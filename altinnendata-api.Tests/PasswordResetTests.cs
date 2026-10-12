using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Moq;
using altinnendata_api.Features.Auth;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Services;
using Xunit;

namespace altinnendata_api.Tests;

public class PasswordResetTests : TestBase
{
    private static string Hash(string text) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public async Task Request_UnknownEmail_ReturnsGenericAndSendsNothing()
    {
        await using var db = CreateDbContext();
        var email = new Mock<IEmailService>();
        MockUserManager.Setup(m => m.FindByEmailAsync("nobody@example.com")).ReturnsAsync((User?)null);

        var result = await PasswordReset.Request(new RequestPasswordResetDto("nobody@example.com"), db, MockUserManager.Object, email.Object, Configuration, default);

        Assert.IsType<Ok<MessageResponse>>(result);
        email.Verify(e => e.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Request_KnownEmail_StoresHashAndSends()
    {
        await using var db = CreateDbContext();
        var email = new Mock<IEmailService>();
        var user = MakeUser();
        MockUserManager.Setup(m => m.FindByEmailAsync(user.Email!)).ReturnsAsync(user);
        MockUserManager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

        Assert.IsType<Ok<MessageResponse>>(await PasswordReset.Request(new RequestPasswordResetDto(user.Email!), db, MockUserManager.Object, email.Object, Configuration, default));

        Assert.NotNull(user.PasswordResetCodeHash);
        Assert.NotNull(user.PasswordResetCodeExpiration);
        email.Verify(e => e.SendEmailAsync(user.Email!, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task Reset_ValidCode_SucceedsAndClearsState()
    {
        var user = MakeUser();
        user.PasswordResetCodeHash = Hash("ABC123");
        user.PasswordResetCodeExpiration = DateTime.UtcNow.AddMinutes(10);
        MockUserManager.Setup(m => m.FindByEmailAsync(user.Email!)).ReturnsAsync(user);
        MockUserManager.Setup(m => m.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("tok");
        MockUserManager.Setup(m => m.ResetPasswordAsync(user, "tok", "newpass1")).ReturnsAsync(IdentityResult.Success);

        Assert.IsType<Ok<MessageResponse>>(await PasswordReset.Reset(new ResetPasswordDto(user.Email!, "ABC123", "newpass1"), MockUserManager.Object, AuthServices(CreateDbContext()).RefreshTokens));
        Assert.Null(user.PasswordResetCodeHash);
        Assert.Equal(0, user.PasswordResetAttempts);
    }

    [Fact]
    public async Task Reset_ValidCode_SignsOutEveryDevice()
    {
        await using var db = CreateDbContext();
        var user = MakeUser();
        user.PasswordResetCodeHash = Hash("ABC123");
        user.PasswordResetCodeExpiration = DateTime.UtcNow.AddMinutes(10);
        MockUserManager.Setup(m => m.FindByEmailAsync(user.Email!)).ReturnsAsync(user);
        MockUserManager.Setup(m => m.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("tok");
        MockUserManager.Setup(m => m.ResetPasswordAsync(user, "tok", "newpass1")).ReturnsAsync(IdentityResult.Success);
        var refreshTokens = AuthServices(db).RefreshTokens;
        await refreshTokens.IssueAsync(user.Id);
        await refreshTokens.IssueAsync(user.Id);
        await refreshTokens.IssueAsync("someone-else");
        await db.SaveChangesAsync();

        await PasswordReset.Reset(new ResetPasswordDto(user.Email!, "ABC123", "newpass1"), MockUserManager.Object, refreshTokens);

        Assert.Equal("someone-else", Assert.Single(db.RefreshTokens).UserId);
    }

    [Fact]
    public async Task Reset_WrongCode_KeepsTheSessions()
    {
        await using var db = CreateDbContext();
        var user = MakeUser();
        user.PasswordResetCodeHash = Hash("ABC123");
        user.PasswordResetCodeExpiration = DateTime.UtcNow.AddMinutes(10);
        MockUserManager.Setup(m => m.FindByEmailAsync(user.Email!)).ReturnsAsync(user);
        MockUserManager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
        var refreshTokens = AuthServices(db).RefreshTokens;
        await refreshTokens.IssueAsync(user.Id);
        await db.SaveChangesAsync();

        await PasswordReset.Reset(new ResetPasswordDto(user.Email!, "WRONG1", "newpass1"), MockUserManager.Object, refreshTokens);

        Assert.Single(db.RefreshTokens);
    }

    [Fact]
    public async Task Reset_ValidCode_LiftsTheLockoutFailedLoginsCaused()
    {
        var user = MakeUser();
        user.PasswordResetCodeHash = Hash("ABC123");
        user.PasswordResetCodeExpiration = DateTime.UtcNow.AddMinutes(10);
        user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
        user.AccessFailedCount = 5;
        MockUserManager.Setup(m => m.FindByEmailAsync(user.Email!)).ReturnsAsync(user);
        MockUserManager.Setup(m => m.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("tok");
        MockUserManager.Setup(m => m.ResetPasswordAsync(user, "tok", "newpass1")).ReturnsAsync(IdentityResult.Success);

        Assert.IsType<Ok<MessageResponse>>(await PasswordReset.Reset(new ResetPasswordDto(user.Email!, "ABC123", "newpass1"), MockUserManager.Object, AuthServices(CreateDbContext()).RefreshTokens));
        Assert.Null(user.LockoutEnd);
        Assert.Equal(0, user.AccessFailedCount);
    }

    [Fact]
    public async Task Reset_BadCode_LeavesTheLockoutInPlace()
    {
        var user = MakeUser();
        DateTimeOffset lockedUntil = DateTimeOffset.UtcNow.AddMinutes(15);
        user.PasswordResetCodeHash = Hash("ABC123");
        user.PasswordResetCodeExpiration = DateTime.UtcNow.AddMinutes(10);
        user.LockoutEnd = lockedUntil;
        MockUserManager.Setup(m => m.FindByEmailAsync(user.Email!)).ReturnsAsync(user);
        MockUserManager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

        await PasswordReset.Reset(new ResetPasswordDto(user.Email!, "WRONG", "newpass1"), MockUserManager.Object, AuthServices(CreateDbContext()).RefreshTokens);

        Assert.Equal(lockedUntil, user.LockoutEnd);
    }

    [Fact]
    public async Task Reset_BadCode_BumpsAttemptsAndReturns400()
    {
        var user = MakeUser();
        user.PasswordResetCodeHash = Hash("ABC123");
        user.PasswordResetCodeExpiration = DateTime.UtcNow.AddMinutes(10);
        MockUserManager.Setup(m => m.FindByEmailAsync(user.Email!)).ReturnsAsync(user);
        MockUserManager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

        var problem = Assert.IsType<ProblemHttpResult>(await PasswordReset.Reset(new ResetPasswordDto(user.Email!, "WRONG", "newpass1"), MockUserManager.Object, AuthServices(CreateDbContext()).RefreshTokens));
        Assert.Equal(400, problem.StatusCode);
        Assert.Equal(1, user.PasswordResetAttempts);
        MockUserManager.Verify(m => m.UpdateAsync(user), Times.Once);
    }

    [Fact]
    public async Task Reset_TooManyAttempts_Returns429()
    {
        var user = MakeUser();
        user.PasswordResetAttempts = 5;
        MockUserManager.Setup(m => m.FindByEmailAsync(user.Email!)).ReturnsAsync(user);

        var problem = Assert.IsType<ProblemHttpResult>(await PasswordReset.Reset(new ResetPasswordDto(user.Email!, "ABC123", "newpass1"), MockUserManager.Object, AuthServices(CreateDbContext()).RefreshTokens));
        Assert.Equal(429, problem.StatusCode);
    }

    [Fact]
    public async Task Reset_UnknownEmail_Returns400()
    {
        MockUserManager.Setup(m => m.FindByEmailAsync("nobody@example.com")).ReturnsAsync((User?)null);
        var problem = Assert.IsType<ProblemHttpResult>(await PasswordReset.Reset(new ResetPasswordDto("nobody@example.com", "ABC123", "newpass1"), MockUserManager.Object, AuthServices(CreateDbContext()).RefreshTokens));
        Assert.Equal(400, problem.StatusCode);
    }
}
