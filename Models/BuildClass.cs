using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace altinnendata_api.Models
{
    /// <summary>What tier of machine a build is — budsjett, mellomklasse, high-end — with the short "who is this for" line shown beside it.</summary>
    public class BuildClass
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Stable machine key, e.g. "budsjett". Labels live in the translations.</summary>
        [Required]
        [MaxLength(60)]
        public required string Key { get; set; }

        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<BuildClassTranslation> Translations { get; set; } = [];
    }

    public class BuildClassTranslation
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int BuildClassId { get; set; }

        [ForeignKey(nameof(BuildClassId))]
        public BuildClass? BuildClass { get; set; }

        [Required]
        [MaxLength(10)]
        public required string Locale { get; set; }

        [Required]
        [MaxLength(100)]
        public required string Name { get; set; }

        [MaxLength(300)]
        public string? Description { get; set; }
    }
}
