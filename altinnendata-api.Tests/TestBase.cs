using Mapster;
using MapsterMapper;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using altinnendata_api.Constants;
using altinnendata_api.Models;
using altinnendata_api.Profiles;
using altinnendata_api.Services;
using System.Security.Claims;

namespace altinnendata_api.Tests;

public abstract class TestBase
{
    protected const string TestJwtKey = "test-secret-key-that-is-long-enough-for-hmacsha256-algorithm!";
    protected const string TestJwtIssuer = "test-issuer";

    protected Mock<UserManager<User>> MockUserManager { get; }
    protected Mock<SignInManager<User>> MockSignInManager { get; }
    protected Mock<IMapper> MockMapper { get; } = new();

    protected static IMapper RealMapper { get; } = BuildRealMapper();

    protected IConfiguration Configuration { get; }

    private static IMapper BuildRealMapper()
    {
        var config = new TypeAdapterConfig();
        new MappingProfile().Register(config);
        return new Mapper(config);
    }

    protected TestBase()
    {
        MockUserManager = CreateUserManagerMock();
        MockSignInManager = CreateSignInManagerMock(MockUserManager);
        Configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = TestJwtKey,
                ["Jwt:Issuer"] = TestJwtIssuer
            })
            .Build();
    }

    protected static ApplicationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);


    protected static ControllerContext MakeControllerContext(string userId = "user-1", bool isAdmin = false)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (isAdmin)
            claims.Add(new(ClaimTypes.Role, RoleNames.Admin));
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
    }

    protected static User MakeUser(string id = "user-1", string email = "test@example.com") => new()
    {
        Id = id,
        Email = email,
        UserName = "testuser",
        FirstName = "Test",
        LastName = "User",
        EmailConfirmed = true
    };

    private static Mock<UserManager<User>> CreateUserManagerMock()
    {
        var store = new Mock<IUserStore<User>>();
        return new Mock<UserManager<User>>(
            store.Object,
            Mock.Of<IOptions<IdentityOptions>>(),
            Mock.Of<IPasswordHasher<User>>(),
            Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(),
            Mock.Of<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            Mock.Of<IServiceProvider>(),
            NullLogger<UserManager<User>>.Instance);
    }

    private static Mock<SignInManager<User>> CreateSignInManagerMock(Mock<UserManager<User>> um) =>
        new(um.Object,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<User>>(),
            Mock.Of<IOptions<IdentityOptions>>(),
            NullLogger<SignInManager<User>>.Instance,
            Mock.Of<IAuthenticationSchemeProvider>(),
            Mock.Of<IUserConfirmation<User>>());
}
