using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using altinnendata_api.Features.BuildClasses;
using altinnendata_api.Models;
using Xunit;

namespace altinnendata_api.Tests;

public class BuildClassTests : TestBase
{
    private static BuildClassInput Input(string key = "budsjett") => new()
    {
        Key = key,
        SortOrder = 10,
        Translations =
        [
            new BuildClassTranslationInput("no", "Budsjett", "Egnet for lettere spill som Minecraft og Roblox."),
            new BuildClassTranslationInput("en", "Budget", "Suited to lighter games such as Minecraft and Roblox.")
        ]
    };

    [Fact]
    public async Task Create_StoresKeyAndTranslations()
    {
        await using var db = CreateDbContext();

        var created = Assert.IsType<Ok<BuildClassDto>>(await BuildClassEndpoints.Create(Input(), db, default));

        Assert.Equal("budsjett", created.Value!.Key);
        Assert.Equal("Budsjett", created.Value.Name);
        Assert.Equal("Egnet for lettere spill som Minecraft og Roblox.", created.Value.Description);
        Assert.Equal(2, created.Value.Translations.Count);
    }

    [Fact]
    public async Task Create_DuplicateKey_IsRejected()
    {
        await using var db = CreateDbContext();
        await BuildClassEndpoints.Create(Input(), db, default);

        var again = await BuildClassEndpoints.Create(Input(), db, default);

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ProblemHttpResult>(again).StatusCode);
    }

    [Fact]
    public async Task GetAll_UsesRequestedLocaleAndFallsBack()
    {
        await using var db = CreateDbContext();
        await BuildClassEndpoints.Create(Input(), db, default);
        await BuildClassEndpoints.Create(new BuildClassInput
        {
            Key = "high-end",
            SortOrder = 30,
            Translations = [new BuildClassTranslationInput("no", "High-end", null)]
        }, db, default);

        var ok = Assert.IsType<Ok<IEnumerable<BuildClassDto>>>(await BuildClassEndpoints.GetAll(db, default, "en"));
        var classes = ok.Value!.ToList();

        Assert.Equal(["Budget", "High-end"], classes.Select(c => c.Name));
        Assert.Null(classes[1].Description);
    }

    [Fact]
    public async Task Delete_LeavesTheBuildWithoutAClass()
    {
        await using var db = CreateDbContext();
        var created = Assert.IsType<Ok<BuildClassDto>>(await BuildClassEndpoints.Create(Input(), db, default));

        db.PcBuilds.Add(new PcBuild
        {
            Slug = "gaming-pc",
            BuildClassId = created.Value!.Id,
            Translations = [new PcBuildTranslation { Locale = "no", Title = "Gaming-PC" }]
        });
        await db.SaveChangesAsync();

        Assert.IsType<NoContent>(await BuildClassEndpoints.Delete(created.Value.Id, db, default));

        var build = await db.PcBuilds.SingleAsync();
        Assert.Null(build.BuildClassId);
    }
}
