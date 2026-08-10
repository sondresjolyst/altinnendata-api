using Microsoft.EntityFrameworkCore;
using altinnendata_api.Models;
using altinnendata_api.Models.Content;

namespace altinnendata_api.Infrastructure
{
    /// <summary>Seeds the component categories a PC build is described with. Existing keys are left alone.</summary>
    public static class SeedData
    {
        private static readonly (string Key, int SortOrder, string No, string En)[] Categories =
        [
            ("cpu", 10, "Prosessor", "Processor"),
            ("cooling", 20, "Kjøling", "Cooling"),
            ("motherboard", 30, "Hovedkort", "Motherboard"),
            ("memory", 40, "Minne", "Memory"),
            ("gpu", 50, "Skjermkort", "Graphics card"),
            ("storage", 60, "Lagring", "Storage"),
            ("psu", 70, "Strømforsyning", "Power supply"),
            ("case", 80, "Kabinett", "Case"),
            ("fans", 90, "Vifter", "Fans"),
            ("os", 100, "Operativsystem", "Operating system")
        ];

        public static async Task EnsureComponentCategoriesAsync(ApplicationDbContext db, CancellationToken ct = default)
        {
            var existing = await db.ComponentCategories.Select(c => c.Key).ToListAsync(ct);
            var missing = Categories.Where(c => !existing.Contains(c.Key)).ToList();
            if (missing.Count == 0) return;

            foreach (var (key, sortOrder, no, en) in missing)
            {
                db.ComponentCategories.Add(new ComponentCategory
                {
                    Key = key,
                    SortOrder = sortOrder,
                    Translations =
                    [
                        new ComponentCategoryTranslation { Locale = "no", Name = no },
                        new ComponentCategoryTranslation { Locale = "en", Name = en }
                    ]
                });
            }

            await db.SaveChangesAsync(ct);
        }

        /// <summary>Writes the starting legal text for any (key, locale) that has none. Edited pages are left alone.</summary>
        public static async Task EnsureLegalPagesAsync(ApplicationDbContext db, CancellationToken ct = default)
        {
            var existing = await db.LegalPages.Select(p => new { p.Key, p.Locale }).ToListAsync(ct);
            var missing = LegalSeedText.All
                .Where(p => !existing.Any(e => e.Key == p.Key && e.Locale == p.Locale))
                .ToList();
            if (missing.Count == 0) return;

            foreach (var page in missing)
            {
                db.LegalPages.Add(new LegalPage
                {
                    Key = page.Key,
                    Locale = page.Locale,
                    Title = page.Title,
                    BodyMarkdown = page.Body
                });
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
