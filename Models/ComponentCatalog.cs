using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace altinnendata_api.Models
{
    /// <summary>Part type: CPU, GPU, hovedkort, minne, lagring, strømforsyning, kabinett, kjøling.</summary>
    public class ComponentCategory
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Stable machine key used by the frontend, e.g. "cpu". Labels live in the translations.</summary>
        [Required]
        [MaxLength(60)]
        public required string Key { get; set; }

        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<ComponentCategoryTranslation> Translations { get; set; } = [];
    }

    public class ComponentCategoryTranslation
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ComponentCategoryId { get; set; }

        [ForeignKey(nameof(ComponentCategoryId))]
        public ComponentCategory? ComponentCategory { get; set; }

        [Required]
        [MaxLength(10)]
        public required string Locale { get; set; }

        [Required]
        [MaxLength(100)]
        public required string Name { get; set; }
    }

    public class ComponentManufacturer
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(80)]
        public required string Name { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ComponentPart
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int CategoryId { get; set; }

        [ForeignKey(nameof(CategoryId))]
        public ComponentCategory? Category { get; set; }

        public int? ManufacturerId { get; set; }

        [ForeignKey(nameof(ManufacturerId))]
        public ComponentManufacturer? Manufacturer { get; set; }

        [Required]
        [MaxLength(160)]
        public required string Name { get; set; }

        [MaxLength(300)]
        public string? Details { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
