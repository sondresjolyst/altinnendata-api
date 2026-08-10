using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace altinnendata_api.Models
{
    public class PcBuildTranslation
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int PcBuildId { get; set; }

        [ForeignKey(nameof(PcBuildId))]
        public PcBuild? PcBuild { get; set; }

        [Required]
        [MaxLength(10)]
        public required string Locale { get; set; }

        [Required]
        [MaxLength(200)]
        public required string Title { get; set; }

        /// <summary>Teaser used on cards, search results and og:description.</summary>
        [MaxLength(400)]
        public string? Summary { get; set; }

        /// <summary>Page body: the same JSON section array the home page editor produces.</summary>
        [Required]
        public string SectionsJson { get; set; } = "[]";
    }
}
