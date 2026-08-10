using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using altinnendata_api.Features.Builds;
using altinnendata_api.Models;
using Xunit;

namespace altinnendata_api.Tests;

public class BuildSlicesTests : TestBase
{
    private static CreateBuildDto Dto(string title = "Gaming-PC 4070", string availability = "Available") => new()
    {
        Category = "gaming",
        Availability = availability,
        PriceNok = 18990,
        Published = true,
        Translations =
        [
            new BuildTranslationInput
            {
                Locale = "no",
                Title = title,
                Summary = "Rask gaming-PC",
                Sections = JsonNode.Parse("""[{"id":"s1","type":"text","heading":"Om byggen","body":"Satt sammen etter kundens behov."}]""")
            }
        ],
        Components =
        [
            new BuildComponentInput { Name = "Ryzen 7 9800X3D", SortOrder = 0 }
        ]
    };

    private static async Task<PcBuild> SeedBuildAsync(ApplicationDbContext db, string slug = "gaming-pc", bool published = true)
    {
        var build = new PcBuild
        {
            Slug = slug,
            Published = published,
            Availability = BuildAvailability.Available,
            Translations =
            [
                new PcBuildTranslation { Locale = "no", Title = "Gaming-PC", Summary = "Rask", SectionsJson = """[{"id":"s1","type":"text","body":"Norsk"}]""" },
                new PcBuildTranslation { Locale = "en", Title = "Gaming PC", Summary = "Fast", SectionsJson = """[{"id":"s1","type":"text","body":"English"}]""" }
            ]
        };
        db.PcBuilds.Add(build);
        await db.SaveChangesAsync();
        return build;
    }

    [Fact]
    public async Task Create_StoresTranslationsAndSlug()
    {
        await using var db = CreateDbContext();

        var created = Assert.IsType<Created<BuildAdminDto>>(await BuildCommands.Create(Dto(), db, default));

        Assert.Equal("gaming-pc-4070", created.Value!.Slug);
        Assert.Single(created.Value.Translations);
        Assert.Equal("Available", created.Value.Availability);
        Assert.Equal(18990, created.Value.PriceNok);
        Assert.Single(created.Value.Components);
    }

    [Fact]
    public async Task Create_GivesDuplicateTitlesDistinctSlugs()
    {
        await using var db = CreateDbContext();

        await BuildCommands.Create(Dto(), db, default);
        var second = Assert.IsType<Created<BuildAdminDto>>(await BuildCommands.Create(Dto(), db, default));

        Assert.Equal("gaming-pc-4070-2", second.Value!.Slug);
    }

    [Fact]
    public async Task GetAll_HidesDraftsFromAnonymousVisitors()
    {
        await using var db = CreateDbContext();
        await SeedBuildAsync(db, "published-build");
        await SeedBuildAsync(db, "draft-build", published: false);

        var http = MakeControllerContext().HttpContext;
        var ok = Assert.IsType<Ok<IEnumerable<BuildSummaryDto>>>(await BuildQueries.GetAll(http, db, default));

        Assert.Single(ok.Value!);
        Assert.Equal("published-build", ok.Value!.Single().Slug);
    }

    [Fact]
    public async Task GetAll_AdminCanRequestDrafts()
    {
        await using var db = CreateDbContext();
        await SeedBuildAsync(db, "published-build");
        await SeedBuildAsync(db, "draft-build", published: false);

        var http = MakeControllerContext(isAdmin: true).HttpContext;
        var ok = Assert.IsType<Ok<IEnumerable<BuildSummaryDto>>>(await BuildQueries.GetAll(http, db, default, all: true));

        Assert.Equal(2, ok.Value!.Count());
    }

    [Fact]
    public async Task GetAll_FiltersByAvailability()
    {
        await using var db = CreateDbContext();
        var sold = await SeedBuildAsync(db, "sold-build");
        sold.Availability = BuildAvailability.Sold;
        await db.SaveChangesAsync();
        await SeedBuildAsync(db, "available-build");

        var http = MakeControllerContext().HttpContext;
        var ok = Assert.IsType<Ok<IEnumerable<BuildSummaryDto>>>(await BuildQueries.GetAll(http, db, default, availability: "sold"));

        Assert.Equal("sold-build", ok.Value!.Single().Slug);
    }

    [Fact]
    public async Task GetBySlug_ReturnsRequestedLocale()
    {
        await using var db = CreateDbContext();
        await SeedBuildAsync(db);
        var http = MakeControllerContext().HttpContext;

        var en = Assert.IsType<Ok<BuildDetailDto>>(await BuildQueries.GetBySlug("gaming-pc", http, db, default, "en"));

        Assert.Equal("Gaming PC", en.Value!.Title);
        Assert.Contains("English", en.Value.Sections.ToJsonString());
        Assert.Equal(["en", "no"], en.Value.AvailableLocales);
    }

    [Fact]
    public async Task GetBySlug_UnknownLocale_FallsBackToDefault()
    {
        await using var db = CreateDbContext();
        await SeedBuildAsync(db);
        var http = MakeControllerContext().HttpContext;

        var ok = Assert.IsType<Ok<BuildDetailDto>>(await BuildQueries.GetBySlug("gaming-pc", http, db, default, "de"));

        Assert.Equal("Gaming-PC", ok.Value!.Title);
        Assert.Equal("no", ok.Value.Locale);
    }

    [Fact]
    public async Task GetBySlug_DraftIsNotFoundForAnonymous()
    {
        await using var db = CreateDbContext();
        await SeedBuildAsync(db, "draft-build", published: false);
        var http = MakeControllerContext().HttpContext;

        Assert.IsType<NotFound>(await BuildQueries.GetBySlug("draft-build", http, db, default));
    }

    [Fact]
    public async Task Update_ReplacesTranslationsAndReslugsOnTitleChange()
    {
        await using var db = CreateDbContext();
        var created = Assert.IsType<Created<BuildAdminDto>>(await BuildCommands.Create(Dto(), db, default));

        var dto = new UpdateBuildDto
        {
            Category = "kontor",
            Availability = "Sold",
            Published = true,
            Translations =
            [
                new BuildTranslationInput { Locale = "no", Title = "Kontor-PC", Sections = JsonNode.Parse("[]") },
                new BuildTranslationInput { Locale = "en", Title = "Office PC", Sections = JsonNode.Parse("[]") }
            ]
        };

        var ok = Assert.IsType<Ok<BuildAdminDto>>(await BuildCommands.Update(created.Value!.Id, dto, db, new FakeImageStorage(), default));

        Assert.Equal("kontor-pc", ok.Value!.Slug);
        Assert.Equal("Sold", ok.Value.Availability);
        Assert.Equal(2, ok.Value.Translations.Count);
        Assert.Equal(2, await db.PcBuildTranslations.CountAsync());
    }

    [Fact]
    public async Task Delete_RemovesBuildAndTranslations()
    {
        await using var db = CreateDbContext();
        var build = await SeedBuildAsync(db);

        Assert.IsType<NoContent>(await BuildCommands.Delete(build.Id, db, new FakeImageStorage(), default));

        Assert.Empty(await db.PcBuilds.ToListAsync());
    }

    [Fact]
    public async Task Delete_UnknownId_ReturnsNotFound()
    {
        await using var db = CreateDbContext();
        Assert.IsType<NotFound>(await BuildCommands.Delete(404, db, new FakeImageStorage(), default));
    }
}
