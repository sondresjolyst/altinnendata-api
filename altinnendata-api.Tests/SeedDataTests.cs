using Microsoft.EntityFrameworkCore;
using altinnendata_api.Constants;
using altinnendata_api.Infrastructure;
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
    public void LegalSeedText_CoversEveryPageInEveryLocale()
    {
        foreach (var key in new[] { "terms", "privacy", "cookies" })
        {
            foreach (var locale in Locales.Supported)
            {
                var page = LegalSeedText.All.SingleOrDefault(p => p.Key == key && p.Locale == locale);
                Assert.NotNull(page);
                Assert.NotEmpty(page!.BodyMarkdown);
                Assert.NotEmpty(page.Title);
            }
        }
    }

}
