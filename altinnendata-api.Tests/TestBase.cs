using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using SjolystInnovation.AppKit.Storage.Images;
using altinnendata_api.Constants;
using System.Security.Claims;
using altinnendata_api.Models;

namespace altinnendata_api.Tests;

/// <summary>The helpers the tests share: an in-memory database, the content image service, and a sample user.</summary>
public abstract class TestBase
{
    protected static ApplicationDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options);

    private static readonly string ImagesRoot = Path.Combine(Path.GetTempPath(), "altinnendata-api-tests", "images");

    /// <summary>The package's content image service on <paramref name="db"/>, with the image files in a temporary directory.</summary>
    protected static IContentImageService CreateImageService(ApplicationDbContext db)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(db);
        services.AddAppKitContentImages<ApplicationDbContext>(o => o.ImagesPath = ImagesRoot);
        return services.BuildServiceProvider().GetRequiredService<IContentImageService>();
    }

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
}
