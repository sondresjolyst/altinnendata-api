using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using altinnendata_api.Features.Content;
using Xunit;

namespace altinnendata_api.Tests;

public class LegalPagesTests : TestBase
{
    private static LegalPageBody Body(string title = "Vilkår") => new(title, "## Vilkår\n\nTekst.");

    [Fact]
    public async Task Put_CreatesThenUpdates()
    {
        await using var db = CreateDbContext();

        var created = Assert.IsType<Ok<LegalPageDto>>(await LegalPages.Put("terms", "no", Body(), db, default));
        Assert.Equal("terms", created.Value!.Key);

        var updated = Assert.IsType<Ok<LegalPageDto>>(await LegalPages.Put("terms", "no", Body("Nye vilkår"), db, default));
        Assert.Equal("Nye vilkår", updated.Value!.Title);
        Assert.Single(db.LegalPages);
    }

    [Fact]
    public async Task Put_UnknownKey_Returns400()
    {
        await using var db = CreateDbContext();
        var problem = Assert.IsType<ProblemHttpResult>(await LegalPages.Put("refunds", "no", Body(), db, default));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [Fact]
    public async Task Put_UnsupportedLocale_Returns400()
    {
        await using var db = CreateDbContext();
        var problem = Assert.IsType<ProblemHttpResult>(await LegalPages.Put("terms", "de", Body(), db, default));
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
    }

    [Fact]
    public async Task Get_FallsBackToDefaultLocale()
    {
        await using var db = CreateDbContext();
        await LegalPages.Put("privacy", "no", Body("Personvern"), db, default);

        var ok = Assert.IsType<Ok<LegalPageDto>>(await LegalPages.Get("privacy", db, default, "en"));
        Assert.Equal("Personvern", ok.Value!.Title);
        Assert.Equal("no", ok.Value.Locale);
    }

    [Fact]
    public async Task Get_Missing_ReturnsNotFound()
    {
        await using var db = CreateDbContext();
        Assert.IsType<NotFound>(await LegalPages.Get("cookies", db, default));
    }
}
