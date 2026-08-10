using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using altinnendata_api.Features.Components;
using altinnendata_api.Models;
using Xunit;

namespace altinnendata_api.Tests;

public class ComponentSlicesTests : TestBase
{
    private static CategoryInput CategoryDto(string key = "cpu") => new()
    {
        Key = key,
        SortOrder = 10,
        Translations = [new CategoryTranslationInput("no", "Prosessor"), new CategoryTranslationInput("en", "Processor")]
    };

    private static async Task<ComponentCategory> SeedCategoryAsync(ApplicationDbContext db, string key = "cpu")
    {
        var category = new ComponentCategory
        {
            Key = key,
            SortOrder = 10,
            Translations =
            [
                new ComponentCategoryTranslation { Locale = "no", Name = "Prosessor" },
                new ComponentCategoryTranslation { Locale = "en", Name = "Processor" }
            ]
        };
        db.ComponentCategories.Add(category);
        await db.SaveChangesAsync();
        return category;
    }

    [Fact]
    public async Task CreateCategory_StoresBothLocales()
    {
        await using var db = CreateDbContext();

        var ok = Assert.IsType<Ok<CategoryDto>>(await Categories.Create(CategoryDto(), db, default));

        Assert.Equal("cpu", ok.Value!.Key);
        Assert.Equal("Prosessor", ok.Value.Name);
        Assert.Equal(2, ok.Value.Translations.Count);
    }

    [Fact]
    public async Task CreateCategory_DuplicateKey_Returns409()
    {
        await using var db = CreateDbContext();
        await SeedCategoryAsync(db);

        var problem = Assert.IsType<ProblemHttpResult>(await Categories.Create(CategoryDto(), db, default));
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task GetCategories_UsesRequestedLocale()
    {
        await using var db = CreateDbContext();
        await SeedCategoryAsync(db);

        var ok = Assert.IsType<Ok<IEnumerable<CategoryDto>>>(await Categories.GetAll(db, default, "en"));
        Assert.Equal("Processor", ok.Value!.Single().Name);
    }

    [Fact]
    public async Task DeleteCategory_WithParts_Returns409()
    {
        await using var db = CreateDbContext();
        var category = await SeedCategoryAsync(db);
        db.ComponentParts.Add(new ComponentPart { CategoryId = category.Id, Name = "Ryzen 7 9800X3D" });
        await db.SaveChangesAsync();

        var problem = Assert.IsType<ProblemHttpResult>(await Categories.Delete(category.Id, db, default));
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task CreateManufacturer_IsIdempotentOnName()
    {
        await using var db = CreateDbContext();

        var first = Assert.IsType<Ok<ManufacturerDto>>(await Manufacturers.Create(new ManufacturerInput("AMD"), db, default));
        var second = Assert.IsType<Ok<ManufacturerDto>>(await Manufacturers.Create(new ManufacturerInput("amd"), db, default));

        Assert.Equal(first.Value!.Id, second.Value!.Id);
        Assert.Single(await db.ComponentManufacturers.ToListAsync());
    }

    [Fact]
    public async Task CreatePart_UnknownCategory_Returns400()
    {
        await using var db = CreateDbContext();

        var problem = Assert.IsType<ProblemHttpResult>(
            await Parts.Create(new PartInput { CategoryId = 99, Name = "Ryzen 7 9800X3D" }, db, default));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [Fact]
    public async Task CreatePart_DuplicateInCategory_Returns409()
    {
        await using var db = CreateDbContext();
        var category = await SeedCategoryAsync(db);
        await Parts.Create(new PartInput { CategoryId = category.Id, Name = "Ryzen 7 9800X3D" }, db, default);

        var problem = Assert.IsType<ProblemHttpResult>(
            await Parts.Create(new PartInput { CategoryId = category.Id, Name = "ryzen 7 9800x3d" }, db, default));
        Assert.Equal(StatusCodes.Status409Conflict, problem.StatusCode);
    }

    [Fact]
    public async Task GetTree_GroupsPartsUnderLocalisedCategories()
    {
        await using var db = CreateDbContext();
        var category = await SeedCategoryAsync(db);
        db.ComponentParts.Add(new ComponentPart { CategoryId = category.Id, Name = "Ryzen 7 9800X3D" });
        await db.SaveChangesAsync();

        var ok = Assert.IsType<Ok<IEnumerable<CategoryTreeDto>>>(await GetComponentTree.Get(db, default, "en"));

        var tree = ok.Value!.Single();
        Assert.Equal("Processor", tree.Name);
        Assert.Equal("Ryzen 7 9800X3D", tree.Parts.Single().Name);
    }
}
