using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using altinnendata_api.Features.ContentImages;
using altinnendata_api.Models;
using Xunit;

namespace altinnendata_api.Tests;

public class ContentImagesSlicesTests : TestBase
{
    [Fact]
    public async Task Upload_StoresFileAndRow()
    {
        await using var db = CreateDbContext();
        var storage = new FakeImageStorage();

        var result = await ContentImages.Upload(FakeImageStorage.MakeImage(), new DefaultHttpContext(), db, storage, default);

        Assert.IsType<Ok<UploadedImage>>(result);
        Assert.Equal(1, storage.SaveCount);
        Assert.Single(db.ContentImages);
    }

    [Fact]
    public async Task Get_Existing_ReturnsFile()
    {
        await using var db = CreateDbContext();
        var storage = new FakeImageStorage();
        storage.Files["x.png"] = [1, 2, 3];
        db.ContentImages.Add(new ContentImage { FileName = "x.png", ContentType = "image/png", StoredPath = "x.png" });
        await db.SaveChangesAsync();
        var image = db.ContentImages.First();

        var result = await ContentImages.Get(image.Id, null, new DefaultHttpContext(), db, storage, default);
        Assert.IsType<FileStreamHttpResult>(result);
    }

    [Fact]
    public async Task Get_Missing_ReturnsNotFound()
    {
        await using var db = CreateDbContext();
        var result = await ContentImages.Get("missing", null, new DefaultHttpContext(), db, new FakeImageStorage(), default);
        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task Upload_GeneratesWebpVariants()
    {
        await using var db = CreateDbContext();
        var storage = new FakeImageStorage();

        await ContentImages.Upload(FakeImageStorage.MakeImage(), new DefaultHttpContext(), db, storage, default);

        Assert.NotEmpty(db.ContentImageVariants);
        Assert.Equal(1, storage.VariantCount);
    }

    [Fact]
    public async Task Get_AcceptsWebp_ServesWebpVariant()
    {
        await using var db = CreateDbContext();
        var storage = new FakeImageStorage();
        storage.Files["x.png"] = [1, 2, 3];
        storage.Files["v.webp"] = [9];
        db.ContentImages.Add(new ContentImage { FileName = "x.png", ContentType = "image/png", StoredPath = "x.png" });
        await db.SaveChangesAsync();
        var image = db.ContentImages.First();
        db.ContentImageVariants.Add(new ContentImageVariant { ContentImageId = image.Id, Width = 800, StoredPath = "v.webp" });
        await db.SaveChangesAsync();

        var http = new DefaultHttpContext();
        http.Request.Headers.Accept = "image/webp,image/*";
        var result = await ContentImages.Get(image.Id, null, http, db, storage, default);

        var file = Assert.IsType<FileStreamHttpResult>(result);
        Assert.Equal("image/webp", file.ContentType);
    }

    [Fact]
    public async Task Get_NoWebpAccept_ServesOriginal()
    {
        await using var db = CreateDbContext();
        var storage = new FakeImageStorage();
        storage.Files["x.png"] = [1, 2, 3];
        db.ContentImages.Add(new ContentImage { FileName = "x.png", ContentType = "image/png", StoredPath = "x.png" });
        await db.SaveChangesAsync();
        var image = db.ContentImages.First();

        var result = await ContentImages.Get(image.Id, null, new DefaultHttpContext(), db, storage, default);

        var file = Assert.IsType<FileStreamHttpResult>(result);
        Assert.Equal("image/png", file.ContentType);
    }

    [Fact]
    public async Task Get_AcceptsWebp_LazilyBackfillsVariants()
    {
        await using var db = CreateDbContext();
        var storage = new FakeImageStorage();
        storage.Files["x.png"] = [1, 2, 3];
        db.ContentImages.Add(new ContentImage { FileName = "x.png", ContentType = "image/png", StoredPath = "x.png" });
        await db.SaveChangesAsync();
        var image = db.ContentImages.First();

        var http = new DefaultHttpContext();
        http.Request.Headers.Accept = "image/webp";
        var result = await ContentImages.Get(image.Id, null, http, db, storage, default);

        var file = Assert.IsType<FileStreamHttpResult>(result);
        Assert.Equal("image/webp", file.ContentType);
        Assert.NotEmpty(db.ContentImageVariants);
    }

    [Fact]
    public async Task Delete_RemovesFileAndRow()
    {
        await using var db = CreateDbContext();
        var storage = new FakeImageStorage();
        storage.Files["x.png"] = [1];
        db.ContentImages.Add(new ContentImage { FileName = "x.png", ContentType = "image/png", StoredPath = "x.png" });
        await db.SaveChangesAsync();
        var image = db.ContentImages.First();

        var result = await ContentImages.Delete(image.Id, db, storage, default);
        Assert.IsType<NoContent>(result);
        Assert.Equal(1, storage.DeleteCount);
        Assert.Empty(db.ContentImages);
    }

    [Fact]
    public async Task Delete_RemovesVariantFiles()
    {
        await using var db = CreateDbContext();
        var storage = new FakeImageStorage();
        storage.Files["x.png"] = [1];
        storage.Files["v.webp"] = [9];
        db.ContentImages.Add(new ContentImage { FileName = "x.png", ContentType = "image/png", StoredPath = "x.png" });
        await db.SaveChangesAsync();
        var image = db.ContentImages.First();
        db.ContentImageVariants.Add(new ContentImageVariant { ContentImageId = image.Id, Width = 800, StoredPath = "v.webp" });
        await db.SaveChangesAsync();

        await ContentImages.Delete(image.Id, db, storage, default);

        Assert.Equal(2, storage.DeleteCount); // original + variant
        Assert.Empty(db.ContentImageVariants);
    }
}
