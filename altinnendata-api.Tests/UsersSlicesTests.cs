using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Moq;
using altinnendata_api.Features.Users;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Models.Auth;
using Xunit;

namespace altinnendata_api.Tests;

public class UsersSlicesTests : TestBase
{
    private HttpContext Http(string userId = "user-1", bool isAdmin = false) =>
        MakeControllerContext(userId: userId, isAdmin: isAdmin).HttpContext;

    [Fact]
    public async Task GetProfile_OwnAccount_ReturnsProfile()
    {
        await using var db = CreateDbContext();
        MockUserManager.Setup(m => m.FindByIdAsync("user-1")).ReturnsAsync(MakeUser());
        var result = await UserProfile.Get("user-1", Http("user-1"), MockUserManager.Object);
        var ok = Assert.IsType<Ok<UserProfileDto>>(result);
        Assert.Equal("user-1", ok.Value!.Id);
    }

    [Fact]
    public async Task GetProfile_OtherUser_Forbidden()
    {
        await using var db = CreateDbContext();
        Assert.IsType<ForbidHttpResult>(await UserProfile.Get("user-2", Http("user-1"), MockUserManager.Object));
    }

    [Fact]
    public async Task GetProfile_OtherUser_AsAdmin_Allowed()
    {
        await using var db = CreateDbContext();
        MockUserManager.Setup(m => m.FindByIdAsync("user-2")).ReturnsAsync(MakeUser("user-2"));
        Assert.IsType<Ok<UserProfileDto>>(await UserProfile.Get("user-2", Http("user-1", isAdmin: true), MockUserManager.Object));
    }

    [Fact]
    public async Task UpdateProfile_ChangesName()
    {
        await using var db = CreateDbContext();
        var user = MakeUser();
        MockUserManager.Setup(m => m.FindByIdAsync("user-1")).ReturnsAsync(user);
        MockUserManager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

        Assert.IsType<Ok<UserProfileDto>>(await UserProfile.Update("user-1", new UpdateProfileDto("New", "Name"), Http("user-1"), MockUserManager.Object));
        Assert.Equal("New", user.FirstName);
    }

    [Fact]
    public async Task ChangePassword_Self_Succeeds()
    {
        var user = MakeUser();
        MockUserManager.Setup(m => m.FindByIdAsync("user-1")).ReturnsAsync(user);
        MockUserManager.Setup(m => m.ChangePasswordAsync(user, "old", "newpass1")).ReturnsAsync(IdentityResult.Success);

        Assert.IsType<Ok<MessageResponse>>(await UserProfile.ChangePassword("user-1", new ChangePasswordDto("old", "newpass1"), Http("user-1"), MockUserManager.Object));
    }

    [Fact]
    public async Task ChangePassword_OtherUser_Forbidden()
    {
        Assert.IsType<ForbidHttpResult>(await UserProfile.ChangePassword("user-2", new ChangePasswordDto("old", "newpass1"), Http("user-1"), MockUserManager.Object));
    }

    [Fact]
    public async Task ChangePassword_WrongCurrent_ValidationProblem()
    {
        var user = MakeUser();
        MockUserManager.Setup(m => m.FindByIdAsync("user-1")).ReturnsAsync(user);
        MockUserManager.Setup(m => m.ChangePasswordAsync(user, "wrong", "newpass1"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Incorrect password." }));

        Assert.IsType<ValidationProblem>(await UserProfile.ChangePassword("user-1", new ChangePasswordDto("wrong", "newpass1"), Http("user-1"), MockUserManager.Object));
    }

    [Fact]
    public async Task DeleteAccount_SoftDeletesAndRevokesTokens()
    {
        await using var db = CreateDbContext();
        var user = MakeUser();
        db.RefreshTokens.Add(new RefreshToken { Token = "t", UserId = "user-1", Expires = DateTime.UtcNow.AddDays(1), Created = DateTime.UtcNow });
        await db.SaveChangesAsync();

        MockUserManager.Setup(m => m.FindByIdAsync("user-1")).ReturnsAsync(user);
        MockUserManager.Setup(m => m.SetEmailAsync(user, It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        MockUserManager.Setup(m => m.SetUserNameAsync(user, It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        MockUserManager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
        MockUserManager.Setup(m => m.RemovePasswordAsync(user)).ReturnsAsync(IdentityResult.Success);

        Assert.IsType<NoContent>(await UserProfile.Delete("user-1", Http("user-1"), MockUserManager.Object, db));
        Assert.True(user.IsDeleted);
        Assert.All(db.RefreshTokens, t => Assert.NotNull(t.Revoked));
    }

    [Fact]
    public async Task ExportData_ReturnsAccount()
    {
        await using var db = CreateDbContext();
        var user = MakeUser();
        MockUserManager.Setup(m => m.FindByIdAsync("user-1")).ReturnsAsync(user);
        MockUserManager.Setup(m => m.GetRolesAsync(user)).ReturnsAsync(new List<string> { "Default" });
        Assert.IsType<Ok<UserExport>>(await UserProfile.Export("user-1", Http("user-1"), MockUserManager.Object));
    }

    [Fact]
    public async Task GetUsers_ReturnsUsersWithRoles()
    {
        await using var db = CreateDbContext();
        db.Users.Add(MakeUser("u1", "a@example.com"));
        await db.SaveChangesAsync();
        MockUserManager.Setup(m => m.GetRolesAsync(It.IsAny<User>())).ReturnsAsync(new List<string> { "Default" });

        var ok = Assert.IsType<Ok<List<AdminUserDto>>>(await UserRoles.GetUsers(db, MockUserManager.Object));
        Assert.Contains("Default", Assert.Single(ok.Value!).Roles);
    }

    [Fact]
    public async Task AddRole_AssignsRole()
    {
        await using var db = CreateDbContext();
        var user = MakeUser("u2", "b@example.com");
        MockUserManager.Setup(m => m.FindByIdAsync("u2")).ReturnsAsync(user);
        MockUserManager.Setup(m => m.IsInRoleAsync(user, "Admin")).ReturnsAsync(false);
        MockUserManager.Setup(m => m.AddToRoleAsync(user, "Admin")).ReturnsAsync(IdentityResult.Success);

        Assert.IsType<Ok<MessageResponse>>(await UserRoles.AddRole("u2", new AssignRoleDto("Admin"), MockUserManager.Object));
    }

    [Fact]
    public async Task AddRole_UnknownRole_BadRequest()
    {
        await using var db = CreateDbContext();
        Assert.IsType<BadRequest<MessageResponse>>(await UserRoles.AddRole("u2", new AssignRoleDto("Wizard"), MockUserManager.Object));
    }

    [Fact]
    public async Task RemoveRole_OwnAdmin_BadRequest()
    {
        await using var db = CreateDbContext();
        Assert.IsType<BadRequest<MessageResponse>>(await UserRoles.RemoveRole("user-1", "Admin", Http("user-1", isAdmin: true), MockUserManager.Object));
    }

    [Fact]
    public async Task RemoveRole_OtherUser_NoContent()
    {
        await using var db = CreateDbContext();
        var user = MakeUser("u2", "b@example.com");
        MockUserManager.Setup(m => m.FindByIdAsync("u2")).ReturnsAsync(user);
        MockUserManager.Setup(m => m.RemoveFromRoleAsync(user, "Admin")).ReturnsAsync(IdentityResult.Success);

        Assert.IsType<NoContent>(await UserRoles.RemoveRole("u2", "Admin", Http("user-1", isAdmin: true), MockUserManager.Object));
    }
}
