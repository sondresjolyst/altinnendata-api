using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models;
using Xunit;

namespace altinnendata_api.Tests;

public class AdminSeederTests : TestBase
{
    private IServiceProvider Services() =>
        new ServiceCollection().AddSingleton(MockUserManager.Object).BuildServiceProvider();

    private static IConfiguration Config(string? email = "first@altinnendata.no", string? password = "Passord123") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Seed:AdminEmail"] = email,
                ["Seed:AdminPassword"] = password
            })
            .Build();

    [Fact]
    public async Task CreatesFirstAdminWhenNoneExists()
    {
        MockUserManager.Setup(m => m.GetUsersInRoleAsync(RoleNames.Admin)).ReturnsAsync([]);
        MockUserManager.Setup(m => m.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
        MockUserManager.Setup(m => m.CreateAsync(It.IsAny<User>(), "Passord123")).ReturnsAsync(IdentityResult.Success);
        MockUserManager.Setup(m => m.AddToRoleAsync(It.IsAny<User>(), RoleNames.Admin)).ReturnsAsync(IdentityResult.Success);

        await AdminSeeder.EnsureFirstAdminAsync(Services(), Config(), NullLogger.Instance);

        MockUserManager.Verify(m => m.CreateAsync(It.IsAny<User>(), "Passord123"), Times.Once);
        MockUserManager.Verify(m => m.AddToRoleAsync(It.IsAny<User>(), RoleNames.Admin), Times.Once);
    }

    [Fact]
    public async Task DoesNothingWhenAnAdminAlreadyExists()
    {
        MockUserManager.Setup(m => m.GetUsersInRoleAsync(RoleNames.Admin)).ReturnsAsync([MakeUser()]);

        await AdminSeeder.EnsureFirstAdminAsync(Services(), Config(), NullLogger.Instance);

        MockUserManager.Verify(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DoesNothingWhenNotConfigured()
    {
        await AdminSeeder.EnsureFirstAdminAsync(Services(), Config(email: null, password: null), NullLogger.Instance);

        MockUserManager.Verify(m => m.GetUsersInRoleAsync(It.IsAny<string>()), Times.Never);
        MockUserManager.Verify(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task PromotesAnExistingUserInsteadOfCreatingOne()
    {
        var existing = MakeUser("u-1", "first@altinnendata.no");
        MockUserManager.Setup(m => m.GetUsersInRoleAsync(RoleNames.Admin)).ReturnsAsync([]);
        MockUserManager.Setup(m => m.FindByEmailAsync("first@altinnendata.no")).ReturnsAsync(existing);
        MockUserManager.Setup(m => m.AddToRoleAsync(existing, RoleNames.Admin)).ReturnsAsync(IdentityResult.Success);

        await AdminSeeder.EnsureFirstAdminAsync(Services(), Config(), NullLogger.Instance);

        MockUserManager.Verify(m => m.AddToRoleAsync(existing, RoleNames.Admin), Times.Once);
        MockUserManager.Verify(m => m.CreateAsync(It.IsAny<User>(), It.IsAny<string>()), Times.Never);
    }
}
