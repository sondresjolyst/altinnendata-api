using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Moq;
using altinnendata_api.Constants;
using altinnendata_api.Features.Users;
using altinnendata_api.Models;
using Xunit;

namespace altinnendata_api.Tests;

public class UserDeleteTests : TestBase
{
    private HttpContext AdminContext() => MakeControllerContext("admin-1", isAdmin: true).HttpContext;

    private void SetupDelete(User target, params User[] admins)
    {
        MockUserManager.Setup(m => m.FindByIdAsync(target.Id)).ReturnsAsync(target);
        MockUserManager.Setup(m => m.IsInRoleAsync(target, RoleNames.Admin)).ReturnsAsync(admins.Contains(target));
        MockUserManager.Setup(m => m.GetUsersInRoleAsync(RoleNames.Admin)).ReturnsAsync(admins);
        MockUserManager.Setup(m => m.SetEmailAsync(target, It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        MockUserManager.Setup(m => m.SetUserNameAsync(target, It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        MockUserManager.Setup(m => m.UpdateAsync(target)).ReturnsAsync(IdentityResult.Success);
        MockUserManager.Setup(m => m.RemovePasswordAsync(target)).ReturnsAsync(IdentityResult.Success);
    }

    [Fact]
    public async Task Delete_ScrubsThePersonalDataOfANonAdmin()
    {
        await using var db = CreateDbContext();
        var target = MakeUser("u-2", "kunde@example.no");
        SetupDelete(target, MakeUser("admin-1", "admin@example.no"));

        var result = await UserProfile.Delete(target.Id, AdminContext(), MockUserManager.Object, db);

        Assert.IsType<NoContent>(result);
        Assert.True(target.IsDeleted);
        Assert.Equal("Deleted", target.FirstName);
        MockUserManager.Verify(m => m.RemovePasswordAsync(target), Times.Once);
    }

    [Fact]
    public async Task Delete_RefusesTheLastAdmin()
    {
        await using var db = CreateDbContext();
        var onlyAdmin = MakeUser("admin-1", "admin@example.no");
        SetupDelete(onlyAdmin, onlyAdmin);

        var problem = Assert.IsType<ProblemHttpResult>(
            await UserProfile.Delete(onlyAdmin.Id, AdminContext(), MockUserManager.Object, db));

        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.False(onlyAdmin.IsDeleted);
    }

    [Fact]
    public async Task Delete_AllowsAnAdminWhenAnotherRemains()
    {
        await using var db = CreateDbContext();
        var target = MakeUser("admin-2", "second@example.no");
        SetupDelete(target, target, MakeUser("admin-1", "admin@example.no"));

        Assert.IsType<NoContent>(await UserProfile.Delete(target.Id, AdminContext(), MockUserManager.Object, db));
        Assert.True(target.IsDeleted);
    }

    [Fact]
    public async Task Delete_IsForbiddenForSomeoneElsesAccountWithoutAdmin()
    {
        await using var db = CreateDbContext();
        var http = MakeControllerContext("u-1").HttpContext;

        Assert.IsType<ForbidHttpResult>(await UserProfile.Delete("u-2", http, MockUserManager.Object, db));
    }
}
