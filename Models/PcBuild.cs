using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace altinnendata_api.Models
{
    public enum BuildAvailability
    {
        Available,
        Reserved,
        Sold
    }

    public class PcBuild
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(160)]
        public required string Slug { get; set; }

        /// <summary>Free-text category key, e.g. gaming, kontor, streaming, workstation.</summary>
        [MaxLength(60)]
        public string? Category { get; set; }

        public BuildAvailability Availability { get; set; } = BuildAvailability.Available;

        /// <summary>Tier of machine, e.g. budsjett or high-end. Admin-managed, so it is a row rather than an enum.</summary>
        public int? BuildClassId { get; set; }

        [ForeignKey(nameof(BuildClassId))]
        public BuildClass? BuildClass { get; set; }

        public int? PriceNok { get; set; }

        public DateOnly? BuiltOn { get; set; }

        /// <summary>Date the build was sold. Set when availability turns Sold, cleared when it turns back.</summary>
        public DateOnly? SoldOn { get; set; }

        [MaxLength(400)]
        public string? FinnUrl { get; set; }

        public bool Published { get; set; }

        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<PcBuildTranslation> Translations { get; set; } = [];
        public ICollection<PcBuildComponent> Components { get; set; } = [];
        public ICollection<PcBuildImage> Images { get; set; } = [];
    }
}
