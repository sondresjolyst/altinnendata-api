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
    public async Task Upload_RecordsTheImageDimensions()
    {
        await using var db = CreateDbContext();
        var storage = new FakeImageStorage { Dimensions = (1600, 1200) };

        await ContentImages.Upload(FakeImageStorage.MakeImage(), new DefaultHttpContext(), db, storage, default);

        var stored = db.ContentImages.Single();
        Assert.Equal(1600, stored.Width);
        Assert.Equal(1200, stored.Height);
    }

    [Fact]
    public async Task Upload_LeavesDimensionsAtZeroWhenTheImageCannotBeRead()
    {
        await using var db = CreateDbContext();
        var storage = new FakeImageStorage { Dimensions = null };

        await ContentImages.Upload(FakeImageStorage.MakeImage(), new DefaultHttpContext(), db, storage, default);

        var stored = db.ContentImages.Single();
        Assert.Equal(0, stored.Width);
        Assert.Equal(0, stored.Height);
    }

    [Fact]
    public async Task Dimensions_ReturnsDimensionsForTheRequestedImages()
    {
        await using var db = CreateDbContext();
        var storage = new FakeImageStorage();
        storage.Files["a.png"] = [1];
        storage.Files["b.png"] = [1];
        db.ContentImages.AddRange(
            new ContentImage { FileName = "a.png", ContentType = "image/png", StoredPath = "a.png", Width = 800, Height = 600 },
            new ContentImage { FileName = "b.png", ContentType = "image/png", StoredPath = "b.png", Width = 400, Height = 300 });
        await db.SaveChangesAsync();
        var ids = db.ContentImages.Select(i => i.Id).ToList();

        var ok = Assert.IsType<Ok<ImageDimensions[]>>(await ContentImages.Dimensions(string.Join(',', ids), db, storage, default));

        Assert.Equal(2, ok.Value!.Length);
        Assert.Contains(ok.Value, m => m.Width == 800 && m.Height == 600);
        Assert.Equal(0, storage.ProbeCount);
    }

    [Fact]
    public async Task Dimensions_MeasuresAndKeepsDimensionsForOlderImages()
    {
        await using var db = CreateDbContext();
        var storage = new FakeImageStorage { Dimensions = (1024, 768) };
        storage.Files["old.png"] = [1];
        db.ContentImages.Add(new ContentImage { FileName = "old.png", ContentType = "image/png", StoredPath = "old.png" });
        await db.SaveChangesAsync();
        var id = db.ContentImages.Single().Id;

        var ok = Assert.IsType<Ok<ImageDimensions[]>>(await ContentImages.Dimensions(id, db, storage, default));

        Assert.Equal(1024, ok.Value!.Single().Width);
        Assert.Equal(768, db.ContentImages.Single().Height);

        // Measured once: the second request reads what was stored.
        Assert.IsType<Ok<ImageDimensions[]>>(await ContentImages.Dimensions(id, db, storage, default));
        Assert.Equal(1, storage.ProbeCount);
    }

    [Fact]
    public async Task Dimensions_OmitsImagesItCannotMeasure()
    {
        await using var db = CreateDbContext();
        var storage = new FakeImageStorage { Dimensions = null };
        storage.Files["bad.png"] = [1];
        db.ContentImages.Add(new ContentImage { FileName = "bad.png", ContentType = "image/png", StoredPath = "bad.png" });
        await db.SaveChangesAsync();

        var ok = Assert.IsType<Ok<ImageDimensions[]>>(
            await ContentImages.Dimensions(db.ContentImages.Single().Id, db, storage, default));

        Assert.Empty(ok.Value!);
    }

    [Fact]
    public async Task Dimensions_IgnoresUnknownIdsAndAnEmptyRequest()
    {
        await using var db = CreateDbContext();
        var storage = new FakeImageStorage();

        Assert.Empty(Assert.IsType<Ok<ImageDimensions[]>>(await ContentImages.Dimensions("nope,alsonope", db, storage, default)).Value!);
        Assert.Empty(Assert.IsType<Ok<ImageDimensions[]>>(await ContentImages.Dimensions("", db, storage, default)).Value!);
        Assert.Empty(Assert.IsType<Ok<ImageDimensions[]>>(await ContentImages.Dimensions(null, db, storage, default)).Value!);
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
