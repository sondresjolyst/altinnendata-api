using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using altinnendata_api.Features.Builds;
using altinnendata_api.Models;
using Xunit;

namespace altinnendata_api.Tests;

public class BuildGalleryTests : TestBase
{
    private static ContentImage Image(string id) =>
        new() { Id = id, FileName = $"{id}.png", ContentType = "image/png", StoredPath = $"{id}.png" };

    private static CreateBuildDto Dto(params string[] imageIds) => new()
    {
        Availability = "Available",
        Translations = [new BuildTranslationInput { Locale = "no", Title = "Gaming-PC" }],
        ImageIds = [.. imageIds],
    };

    [Fact]
    public async Task Create_KeepsGalleryOrder()
    {
        await using var db = CreateDbContext();
        db.ContentImages.AddRange(Image("a"), Image("b"), Image("c"));
        await db.SaveChangesAsync();

        var created = Assert.IsType<Created<BuildAdminDto>>(await BuildCommands.Create(Dto("c", "a", "b"), db, default));

        Assert.Equal(["c", "a", "b"], created.Value!.ImageIds);
    }

    [Fact]
    public async Task Create_IgnoresARepeatedImage()
    {
        await using var db = CreateDbContext();
        db.ContentImages.Add(Image("a"));
        await db.SaveChangesAsync();

        var created = Assert.IsType<Created<BuildAdminDto>>(await BuildCommands.Create(Dto("a", "a"), db, default));

        Assert.Equal(["a"], created.Value!.ImageIds);
    }

    [Fact]
    public async Task Update_ReplacesTheGallery()
    {
        await using var db = CreateDbContext();
        db.ContentImages.AddRange(Image("a"), Image("b"));
        await db.SaveChangesAsync();
        var created = Assert.IsType<Created<BuildAdminDto>>(await BuildCommands.Create(Dto("a"), db, default));

        var dto = new UpdateBuildDto
        {
            Availability = "Available",
            Translations = [new BuildTranslationInput { Locale = "no", Title = "Gaming-PC" }],
            ImageIds = ["b"],
        };
        var ok = Assert.IsType<Ok<BuildAdminDto>>(await BuildCommands.Update(created.Value!.Id, dto, db, new FakeImageStorage(), default));

        Assert.Equal(["b"], ok.Value!.ImageIds);
        Assert.Equal(1, await db.PcBuildImages.CountAsync());
    }

    [Fact]
    public async Task Update_KeepsACoverImageThatIsAlsoInTheGallery()
    {
        await using var db = CreateDbContext();
        db.ContentImages.Add(Image("a"));
        await db.SaveChangesAsync();

        var create = Dto("a");
        create.CoverImageId = "a";
        var created = Assert.IsType<Created<BuildAdminDto>>(await BuildCommands.Create(create, db, default));

        var dto = new UpdateBuildDto
        {
            Availability = "Available",
            CoverImageId = null,
            Translations = [new BuildTranslationInput { Locale = "no", Title = "Gaming-PC" }],
            ImageIds = ["a"],
        };
        var storage = new FakeImageStorage();
        Assert.IsType<Ok<BuildAdminDto>>(await BuildCommands.Update(created.Value!.Id, dto, db, storage, default));

        Assert.Equal(0, storage.DeleteCount);
        Assert.NotNull(await db.ContentImages.FindAsync("a"));
    }

    [Theory]
    [InlineData("https://www.finn.no/recommerce/forsale/item/123", true)]
    [InlineData("https://finn.no/item/1", true)]
    [InlineData("http://www.finn.no/item/1", false)]
    [InlineData("https://finn.no.evil.example/item/1", false)]
    [InlineData("https://example.com/item/1", false)]
    public void Validator_OnlyAcceptsHttpsFinnLinks(string url, bool valid)
    {
        var dto = Dto();
        dto.FinnUrl = url;

        Assert.Equal(valid, new CreateBuildValidator().Validate(dto).IsValid);
    }
}
