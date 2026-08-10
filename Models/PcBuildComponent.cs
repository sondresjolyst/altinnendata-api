using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace altinnendata_api.Models
{
    /// <summary>One line of a build's parts list. Either points at a catalog part or carries a free-text name.</summary>
    public class PcBuildComponent
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int PcBuildId { get; set; }

        [ForeignKey(nameof(PcBuildId))]
        public PcBuild? PcBuild { get; set; }

        public int? ComponentPartId { get; set; }

        [ForeignKey(nameof(ComponentPartId))]
        public ComponentPart? ComponentPart { get; set; }

        public int? ComponentCategoryId { get; set; }

        [ForeignKey(nameof(ComponentCategoryId))]
        public ComponentCategory? ComponentCategory { get; set; }

        [MaxLength(200)]
        public string? Name { get; set; }

        [MaxLength(300)]
        public string? Details { get; set; }

        public int SortOrder { get; set; }
    }
}
