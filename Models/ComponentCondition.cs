using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace altinnendata_api.Models
{
    /// <summary>What state a part in a build is in (ny, brukt), shown beside it in the spec list.</summary>
    public class ComponentCondition
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Stable machine key, e.g. "brukt". Labels live in the translations.</summary>
        [Required]
        [MaxLength(60)]
        public required string Key { get; set; }

        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<ComponentConditionTranslation> Translations { get; set; } = [];
    }

    public class ComponentConditionTranslation
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ComponentConditionId { get; set; }

        [ForeignKey(nameof(ComponentConditionId))]
        public ComponentCondition? ComponentCondition { get; set; }

        [Required]
        [MaxLength(10)]
        public required string Locale { get; set; }

        [Required]
        [MaxLength(100)]
        public required string Name { get; set; }
    }
}
