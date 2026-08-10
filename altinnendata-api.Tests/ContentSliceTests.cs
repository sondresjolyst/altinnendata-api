using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using altinnendata_api.Features.Content;
using Xunit;

namespace altinnendata_api.Tests;

public class ContentSliceTests : TestBase
{
    private static JsonElement Sections(string json) => JsonSerializer.Deserialize<JsonElement>(json);

    [Fact]
    public async Task GetHome_NoContent_ReturnsEmptyArray()
    {
        await using var db = CreateDbContext();
        var content = Assert.IsType<ContentHttpResult>(await HomeContent.Get(db, default));
        Assert.Equal("[]", content.ResponseContent);
        Assert.Equal("application/json", content.ContentType);
    }

    [Fact]
    public async Task PutHome_PersistsSectionsForDefaultLocale()
    {
        await using var db = CreateDbContext();

        var content = Assert.IsType<ContentHttpResult>(
            await HomeContent.Put(Sections("""[{"id":"1","type":"hero","heading":"Hei"}]"""), db, default));
        Assert.Contains("hero", content.ResponseContent!);

        var stored = await db.HomePageContents.SingleAsync();
        Assert.Equal("no", stored.Locale);
        Assert.Contains("Hei", stored.SectionsJson);
    }

    [Fact]
    public async Task PutHome_KeepsLocalesApart()
    {
        await using var db = CreateDbContext();

        await HomeContent.Put(Sections("""[{"id":"1","type":"hero","heading":"Hei"}]"""), db, default, "no");
        await HomeContent.Put(Sections("""[{"id":"1","type":"hero","heading":"Hello"}]"""), db, default, "en");

        var no = Assert.IsType<ContentHttpResult>(await HomeContent.Get(db, default, "no"));
        var en = Assert.IsType<ContentHttpResult>(await HomeContent.Get(db, default, "en"));

        Assert.Contains("Hei", no.ResponseContent!);
        Assert.Contains("Hello", en.ResponseContent!);
        Assert.Equal(2, await db.HomePageContents.CountAsync());
    }

    [Fact]
    public async Task GetHome_UnfilledLocale_FallsBackToDefault()
    {
        await using var db = CreateDbContext();
        await HomeContent.Put(Sections("""[{"id":"1","type":"hero","heading":"Hei"}]"""), db, default, "no");

        var en = Assert.IsType<ContentHttpResult>(await HomeContent.Get(db, default, "en"));
        Assert.Contains("Hei", en.ResponseContent!);
    }

    [Fact]
    public async Task PutHome_RejectsUnsupportedLocale()
    {
        await using var db = CreateDbContext();
        Assert.IsType<ProblemHttpResult>(await HomeContent.Put(Sections("[]"), db, default, "de"));
    }

    [Fact]
    public async Task PutHome_RejectsNonArray()
    {
        await using var db = CreateDbContext();
        Assert.IsType<ProblemHttpResult>(await HomeContent.Put(Sections("""{"foo":"bar"}"""), db, default));
    }
}
