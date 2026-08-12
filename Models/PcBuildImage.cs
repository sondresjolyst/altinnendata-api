using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace altinnendata_api.Models
{
    public class PcBuildImage
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int PcBuildId { get; set; }

        [ForeignKey(nameof(PcBuildId))]
        public PcBuild? PcBuild { get; set; }

        [Required]
        [MaxLength(32)]
        public required string ContentImageId { get; set; }

        [ForeignKey(nameof(ContentImageId))]
        public ContentImage? ContentImage { get; set; }

        public int SortOrder { get; set; }
    }
}
