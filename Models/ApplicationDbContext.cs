using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SjolystInnovation.AppKit.SiteSettings;
using altinnendata_api.Models.Admin;
using altinnendata_api.Models.Shop;
using SjolystInnovation.AppKit.Auth;
using SjolystInnovation.AppKit.Storage.Images;
using SjolystInnovation.AppKit.Vipps;

namespace altinnendata_api.Models
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : IdentityDbContext<User, IdentityRole, string>(options), IAuthDbContext, IVippsWebhookDbContext, IDataProtectionKeyContext
    {
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<AppSettings> AppSettings { get; set; }
        public DbSet<PageSections> PageSections { get; set; }
        public DbSet<LegalPage> LegalPages { get; set; }
        public DbSet<PcBuild> PcBuilds { get; set; }
        public DbSet<PcBuildTranslation> PcBuildTranslations { get; set; }
        public DbSet<PcBuildComponent> PcBuildComponents { get; set; }
        public DbSet<PcBuildImage> PcBuildImages { get; set; }
        public DbSet<BuildClass> BuildClasses { get; set; }
        public DbSet<BuildClassTranslation> BuildClassTranslations { get; set; }
        public DbSet<ComponentCondition> ComponentConditions { get; set; }
        public DbSet<ComponentConditionTranslation> ComponentConditionTranslations { get; set; }
        public DbSet<ComponentCategory> ComponentCategories { get; set; }
        public DbSet<ComponentCategoryTranslation> ComponentCategoryTranslations { get; set; }
        public DbSet<ComponentManufacturer> ComponentManufacturers { get; set; }
        public DbSet<ComponentPart> ComponentParts { get; set; }
        public DbSet<ContentImage> ContentImages { get; set; }
        public DbSet<ContentImageVariant> ContentImageVariants { get; set; }
        public DbSet<DailyStatSnapshot> DailyStatSnapshots { get; set; }
        public DbSet<ShopSettings> ShopSettings { get; set; }
        public DbSet<DeliveryOption> DeliveryOptions { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<VippsProcessedWebhookEvent> ProcessedWebhookEvents { get; set; }
        public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.AddAppKitRefreshTokens<User>();
            modelBuilder.AddAppKitContentImages();

            modelBuilder.Entity<DailyStatSnapshot>()
                .HasIndex(s => s.Date)
                .IsUnique();

            modelBuilder.AddAppKitLegalPages();
            modelBuilder.AddAppKitVippsProcessedWebhookEvents();
            modelBuilder.AddAppKitVippsWebhookRegistrations();

            modelBuilder.Entity<ShopSettings>()
                .Property(s => s.Id)
                .ValueGeneratedNever();

            modelBuilder.Entity<ShopSettings>()
                .Property(s => s.BuyButton)
                .HasConversion<string>()
                .HasMaxLength(20);

            modelBuilder.Entity<DeliveryOption>()
                .Property(o => o.Method)
                .HasConversion<string>()
                .HasMaxLength(20);

            modelBuilder.Entity<DeliveryOption>()
                .HasIndex(o => o.Method)
                .IsUnique();

            modelBuilder.Entity<DeliveryOption>().Property(o => o.LabelNo).HasMaxLength(80);
            modelBuilder.Entity<DeliveryOption>().Property(o => o.LabelEn).HasMaxLength(80);
            modelBuilder.Entity<DeliveryOption>().Property(o => o.DescriptionNo).HasMaxLength(300);
            modelBuilder.Entity<DeliveryOption>().Property(o => o.DescriptionEn).HasMaxLength(300);

            modelBuilder.Entity<Order>(order =>
            {
                order.Property(o => o.Reference).HasMaxLength(64);
                order.HasIndex(o => o.Reference).IsUnique();
                order.Property(o => o.PublicToken).HasMaxLength(64);
                order.HasIndex(o => o.PublicToken).IsUnique();
                order.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
                order.Property(o => o.Locale).HasMaxLength(5);
                order.Property(o => o.BuyerName).HasMaxLength(120);
                order.Property(o => o.BuyerEmail).HasMaxLength(200);
                order.Property(o => o.BuyerPhone).HasMaxLength(30);
                order.Property(o => o.StreetAddress).HasMaxLength(200);
                order.Property(o => o.PostalCode).HasMaxLength(10);
                order.Property(o => o.City).HasMaxLength(100);
                order.Property(o => o.DeliveryMethod).HasConversion<string>().HasMaxLength(20);
                order.Property(o => o.DeliveryLabel).HasMaxLength(80);
                order.Property(o => o.PickupNote).HasMaxLength(500);
                order.Property(o => o.BuildTitle).HasMaxLength(200);
                order.Property(o => o.BuildSlug).HasMaxLength(160);
                order.Property(o => o.InvoiceNumber).HasMaxLength(50);
                order.HasIndex(o => o.CreatedAt);
                order.Property(o => o.Version).IsRowVersion();

                // Deleting a build keeps its orders, which carry their own copy of the build details.
                order.HasOne(o => o.PcBuild)
                    .WithMany()
                    .HasForeignKey(o => o.PcBuildId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<PcBuild>()
                .Property(b => b.Version)
                .IsRowVersion();

            modelBuilder.Entity<PcBuild>()
                .HasOne<Order>()
                .WithMany()
                .HasForeignKey(b => b.HeldByOrderId)
                .OnDelete(DeleteBehavior.SetNull);

            // The home page sections keep their existing table, which holds one page per locale.
            modelBuilder.AddAppKitPageSections(o =>
            {
                o.TableName = "HomePageContents";
                o.KeyColumn = false;
            });

            modelBuilder.Entity<PcBuild>()
                .HasIndex(b => b.Slug)
                .IsUnique();

            modelBuilder.Entity<PcBuild>()
                .Property(b => b.Availability)
                .HasConversion<string>()
                .HasMaxLength(20);

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

            modelBuilder.Entity<PcBuildImage>()
                .HasIndex(i => new { i.PcBuildId, i.ContentImageId })
                .IsUnique();

            modelBuilder.Entity<PcBuildImage>()
                .HasOne(i => i.PcBuild)
                .WithMany(b => b.Images)
                .HasForeignKey(i => i.PcBuildId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PcBuildImage>()
                .HasOne(i => i.ContentImage)
                .WithMany()
                .HasForeignKey(i => i.ContentImageId)
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

            modelBuilder.Entity<BuildClass>()
                .HasIndex(c => c.Key)
                .IsUnique();

            modelBuilder.Entity<BuildClassTranslation>()
                .HasIndex(t => new { t.BuildClassId, t.Locale })
                .IsUnique();

            modelBuilder.Entity<BuildClassTranslation>()
                .HasOne(t => t.BuildClass)
                .WithMany(c => c.Translations)
                .HasForeignKey(t => t.BuildClassId)
                .OnDelete(DeleteBehavior.Cascade);

            // Deleting a class leaves its builds in place, without one.
            modelBuilder.Entity<PcBuild>()
                .HasOne(b => b.BuildClass)
                .WithMany()
                .HasForeignKey(b => b.BuildClassId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<ComponentCondition>()
                .HasIndex(c => c.Key)
                .IsUnique();

            modelBuilder.Entity<ComponentConditionTranslation>()
                .HasIndex(t => new { t.ComponentConditionId, t.Locale })
                .IsUnique();

            modelBuilder.Entity<ComponentConditionTranslation>()
                .HasOne(t => t.ComponentCondition)
                .WithMany(c => c.Translations)
                .HasForeignKey(t => t.ComponentConditionId)
                .OnDelete(DeleteBehavior.Cascade);

            // Deleting a condition leaves the parts in place, without one.
            modelBuilder.Entity<PcBuildComponent>()
                .HasOne(c => c.Condition)
                .WithMany()
                .HasForeignKey(c => c.ComponentConditionId)
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
        }
    }
}
