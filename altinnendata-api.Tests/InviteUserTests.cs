using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Moq;
using altinnendata_api.Features.Users;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using altinnendata_api.Services;
using Xunit;

namespace altinnendata_api.Tests;

public class InviteUserTests : TestBase
{
    private static InviteUserDto Dto(string role = "Admin") => new("ny@altinnendata.no", "Ny", "Admin", role);

    [Fact]
    public async Task Invite_CreatesUserAssignsRoleAndEmailsCode()
    {
        await using var db = CreateDbContext();
        var email = new Mock<IEmailService>();
        var user = MakeUser("u-new", "ny@altinnendata.no");

        MockMapper.Setup(m => m.Map<User>(It.IsAny<InviteUserDto>())).Returns(user);
        MockUserManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
        MockUserManager.Setup(m => m.CreateAsync(user)).ReturnsAsync(IdentityResult.Success);
        MockUserManager.Setup(m => m.AddToRoleAsync(user, "Admin")).ReturnsAsync(IdentityResult.Success);
        MockUserManager.Setup(m => m.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);

        var result = await InviteUser.Handle(Dto(), db, MockUserManager.Object, email.Object, MockMapper.Object, default);

        Assert.IsType<Ok<MessageResponse>>(result);
        MockUserManager.Verify(m => m.AddToRoleAsync(user, "Admin"), Times.Once);
        email.Verify(e => e.SendEmailAsync("ny@altinnendata.no", It.IsAny<string>(), It.IsAny<string>(), null), Times.Once);
        Assert.NotNull(user.PasswordResetCodeHash);
    }

    [Fact]
    public async Task Invite_ExistingEmail_Returns409()
    {
        await using var db = CreateDbContext();
        MockUserManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(MakeUser());

        var problem = Assert.IsType<ProblemHttpResult>(
            await InviteUser.Handle(Dto(), db, MockUserManager.Object, Mock.Of<IEmailService>(), MockMapper.Object, default));

        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
        MockUserManager.Verify(m => m.CreateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public void Validator_RejectsUnknownRole()
    {
        var validator = new InviteUserValidator();
        Assert.False(validator.Validate(Dto("Superuser")).IsValid);
        Assert.True(validator.Validate(Dto()).IsValid);
    }
}
