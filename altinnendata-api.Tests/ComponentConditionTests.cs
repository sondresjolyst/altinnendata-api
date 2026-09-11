using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using altinnendata_api.Features.ComponentConditions;
using altinnendata_api.Models;
using Xunit;

namespace altinnendata_api.Tests;

public class ComponentConditionTests : TestBase
{
    private static ComponentConditionInput Input(string key = "brukt") => new()
    {
        Key = key,
        SortOrder = 10,
        Translations =
        [
            new ComponentConditionTranslationInput("no", "Brukt"),
            new ComponentConditionTranslationInput("en", "Used")
        ]
    };

    [Fact]
    public async Task Create_StoresKeyAndTranslations()
    {
        await using var db = CreateDbContext();

        var created = Assert.IsType<Ok<ComponentConditionDto>>(await ComponentConditionEndpoints.Create(Input(), db, default));

        Assert.Equal("brukt", created.Value!.Key);
        Assert.Equal("Brukt", created.Value.Name);
        Assert.Equal(2, created.Value.Translations.Count);
    }

    [Fact]
    public async Task Create_DuplicateKey_IsRejected()
    {
        await using var db = CreateDbContext();
        await ComponentConditionEndpoints.Create(Input(), db, default);

        var again = await ComponentConditionEndpoints.Create(Input(), db, default);

        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ProblemHttpResult>(again).StatusCode);
    }

    [Fact]
    public async Task GetAll_UsesRequestedLocaleAndFallsBack()
    {
        await using var db = CreateDbContext();
        await ComponentConditionEndpoints.Create(Input(), db, default);
        await ComponentConditionEndpoints.Create(new ComponentConditionInput
        {
            Key = "ny",
            SortOrder = 20,
            Translations = [new ComponentConditionTranslationInput("no", "Ny")]
        }, db, default);

        var ok = Assert.IsType<Ok<IEnumerable<ComponentConditionDto>>>(await ComponentConditionEndpoints.GetAll(db, default, "en"));

        Assert.Equal(["Used", "Ny"], ok.Value!.Select(c => c.Name));
    }

    [Fact]
    public async Task Delete_LeavesThePartWithoutACondition()
    {
        await using var db = CreateDbContext();
        var created = Assert.IsType<Ok<ComponentConditionDto>>(await ComponentConditionEndpoints.Create(Input(), db, default));

        db.PcBuilds.Add(new PcBuild
        {
            Slug = "gaming-pc",
            Translations = [new PcBuildTranslation { Locale = "no", Title = "Gaming-PC" }],
            Components = [new PcBuildComponent { Name = "RTX 3070", ComponentConditionId = created.Value!.Id }]
        });
        await db.SaveChangesAsync();

        Assert.IsType<NoContent>(await ComponentConditionEndpoints.Delete(created.Value.Id, db, default));

        var component = await db.PcBuildComponents.SingleAsync();
        Assert.Null(component.ComponentConditionId);
    }
}
