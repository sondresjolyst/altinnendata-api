using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using altinnendata_api.Models.Admin;
using altinnendata_api.Models.Auth;
using altinnendata_api.Models.Content;

namespace altinnendata_api.Models
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : IdentityDbContext<User, IdentityRole, string>(options)
    {
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<AppSettings> AppSettings { get; set; }
        public DbSet<HomePageContent> HomePageContents { get; set; }
        public DbSet<LegalPage> LegalPages { get; set; }
        public DbSet<PcBuild> PcBuilds { get; set; }
        public DbSet<PcBuildTranslation> PcBuildTranslations { get; set; }
        public DbSet<PcBuildComponent> PcBuildComponents { get; set; }
        public DbSet<ComponentCategory> ComponentCategories { get; set; }
        public DbSet<ComponentCategoryTranslation> ComponentCategoryTranslations { get; set; }
        public DbSet<ComponentManufacturer> ComponentManufacturers { get; set; }
        public DbSet<ComponentPart> ComponentParts { get; set; }
        public DbSet<ContentImage> ContentImages { get; set; }
        public DbSet<ContentImageVariant> ContentImageVariants { get; set; }
        public DbSet<DailyStatSnapshot> DailyStatSnapshots { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<DailyStatSnapshot>()
                .HasIndex(s => s.Date)
                .IsUnique();

            modelBuilder.Entity<HomePageContent>()
                .HasIndex(c => c.Locale)
                .IsUnique();

            modelBuilder.Entity<LegalPage>()
                .HasIndex(p => new { p.Key, p.Locale })
                .IsUnique();

            modelBuilder.Entity<PcBuild>()
                .HasIndex(b => b.Slug)
                .IsUnique();

            modelBuilder.Entity<PcBuild>()
                .Property(b => b.Availability)
                .HasConversion<string>()
                .HasMaxLength(20);

            modelBuilder.Entity<PcBuild>()
                .HasOne(b => b.CoverImage)
                .WithMany()
                .HasForeignKey(b => b.CoverImageId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<PcBuildTranslation>()
                .HasIndex(t => new { t.PcBuildId, t.Locale })
                .IsUnique();

            modelBuilder.Entity<PcBuildTranslation>()
                .HasOne(t => t.PcBuild)
                .WithMany(b => b.Translations)
                .HasForeignKey(t => t.PcBuildId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PcBuildComponent>()
                .HasOne(c => c.PcBuild)
                .WithMany(b => b.Components)
                .HasForeignKey(c => c.PcBuildId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PcBuildComponent>()
                .HasOne(c => c.ComponentPart)
                .WithMany()
                .HasForeignKey(c => c.ComponentPartId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<PcBuildComponent>()
                .HasOne(c => c.ComponentCategory)
                .WithMany()
                .HasForeignKey(c => c.ComponentCategoryId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<ComponentCategory>()
                .HasIndex(c => c.Key)
                .IsUnique();

            modelBuilder.Entity<ComponentCategoryTranslation>()
                .HasIndex(t => new { t.ComponentCategoryId, t.Locale })
                .IsUnique();

            modelBuilder.Entity<ComponentCategoryTranslation>()
                .HasOne(t => t.ComponentCategory)
                .WithMany(c => c.Translations)
                .HasForeignKey(t => t.ComponentCategoryId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ComponentManufacturer>()
                .HasIndex(m => m.Name)
                .IsUnique();

            modelBuilder.Entity<ComponentPart>()
                .HasIndex(p => new { p.CategoryId, p.ManufacturerId, p.Name })
                .IsUnique();

            modelBuilder.Entity<ComponentPart>()
                .HasOne(p => p.Category)
                .WithMany()
                .HasForeignKey(p => p.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ComponentPart>()
                .HasOne(p => p.Manufacturer)
                .WithMany()
                .HasForeignKey(p => p.ManufacturerId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<ContentImageVariant>()
                .HasOne(v => v.ContentImage)
                .WithMany(i => i.Variants)
                .HasForeignKey(v => v.ContentImageId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
