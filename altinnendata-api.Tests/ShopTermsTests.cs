using SjolystInnovation.AppKit.SiteSettings;
using altinnendata_api.Constants;
using altinnendata_api.Features.Shop;
using altinnendata_api.Models;
using Xunit;

namespace altinnendata_api.Tests;

public class ShopTermsTests : TestBase
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LegalPage Page(string key, string locale, DateTime updatedAt) =>
        new() { Key = key, Locale = locale, Title = locale, BodyMarkdown = $"{key} {locale}", UpdatedAt = updatedAt };

    private static async Task<ApplicationDbContext> DbWith(params LegalPage[] pages)
    {
        var db = CreateDbContext();
        db.LegalPages.AddRange(pages);
        await db.SaveChangesAsync(Ct);
        return db;
    }

    [Theory]
    [InlineData("en-GB", "en")]
    [InlineData("en", "en")]
    [InlineData("nb", "no")]
    [InlineData("nn", "no")]
    [InlineData("no", "no")]
    [InlineData(null, "no")]
    public async Task FindAsync_PicksThePageOfTheLocaleTheCallersPass(string? asked, string expected)
    {
        await using var db = await DbWith(
            Page("terms", "no", new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc)),
            Page("terms", "en", new DateTime(2026, 10, 2, 9, 30, 0, DateTimeKind.Utc)),
            Page("privacy", "en", new DateTime(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc)));

        var page = await ShopTerms.FindAsync(db, Locales.Normalize(asked), Ct);

        Assert.NotNull(page);
        Assert.Equal("terms", page.Key);
        Assert.Equal(expected, page.Locale);
    }

    [Theory]
    [InlineData("en-GB")]
    [InlineData("en")]
    [InlineData("nb")]
    [InlineData("nn")]
    [InlineData("no")]
    [InlineData(null)]
    public async Task FindAsync_ReturnsTheNorwegianPage_WhenThereIsNoEnglishPage(string? asked)
    {
        await using var db = await DbWith(Page("terms", "no", new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc).AddTicks(1234560)));

        var page = await ShopTerms.FindAsync(db, Locales.Normalize(asked), Ct);

        Assert.Equal("no", page?.Locale);
        Assert.Equal("2026-10-01T08:00:00.123456Z", ShopTerms.VersionOf(page));
    }

    [Fact]
    public async Task FindAsync_FindsNothing_AndTheVersionIsNone_WithoutATermsPage()
    {
        await using var db = await DbWith(Page("privacy", "no", new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc)));

        var page = await ShopTerms.FindAsync(db, Locales.Normalize("en"), Ct);

        Assert.Null(page);
        Assert.Equal(ShopTerms.NoTermsVersion, ShopTerms.VersionOf(page));
    }
}
