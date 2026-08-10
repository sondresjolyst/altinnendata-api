using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
using altinnendata_api.Models.Content;
using Xunit;

namespace altinnendata_api.Tests;

public class SeedDataTests : TestBase
{
    [Fact]
    public async Task ComponentCategories_AreSeededOnceWithBothLocales()
    {
        await using var db = CreateDbContext();

        await SeedData.EnsureComponentCategoriesAsync(db);
        var afterFirst = await db.ComponentCategories.CountAsync();

        await SeedData.EnsureComponentCategoriesAsync(db);

        Assert.Equal(afterFirst, await db.ComponentCategories.CountAsync());
        Assert.NotEqual(0, afterFirst);

        var cpu = await db.ComponentCategories.Include(c => c.Translations).FirstAsync(c => c.Key == "cpu");
        Assert.Equal("Prosessor", cpu.Translations.Single(t => t.Locale == "no").Name);
        Assert.Equal("Processor", cpu.Translations.Single(t => t.Locale == "en").Name);
    }

    [Fact]
    public async Task LegalPages_AreSeededForEveryKeyAndLocale()
    {
        await using var db = CreateDbContext();

        await SeedData.EnsureLegalPagesAsync(db);

        foreach (var key in LegalPageKeys.All)
        {
            foreach (var locale in Locales.Supported)
            {
                var page = await db.LegalPages.FirstOrDefaultAsync(p => p.Key == key && p.Locale == locale);
                Assert.NotNull(page);
                Assert.NotEmpty(page!.BodyMarkdown);
            }
        }
    }

    [Fact]
    public async Task LegalPages_DoNotOverwriteEditedText()
    {
        await using var db = CreateDbContext();
        db.LegalPages.Add(new LegalPage { Key = "terms", Locale = "no", Title = "Mine vilkår", BodyMarkdown = "Egen tekst" });
        await db.SaveChangesAsync();

        await SeedData.EnsureLegalPagesAsync(db);

        var page = await db.LegalPages.SingleAsync(p => p.Key == "terms" && p.Locale == "no");
        Assert.Equal("Egen tekst", page.BodyMarkdown);
    }
}
