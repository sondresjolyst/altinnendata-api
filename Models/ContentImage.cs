using System.ComponentModel.DataAnnotations;

namespace altinnendata_api.Models
{
    public class ContentImage
    {
        [Key]
        [MaxLength(32)]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [Required]
        [MaxLength(260)]
        public required string FileName { get; set; }

        [Required]
        [MaxLength(100)]
        public required string ContentType { get; set; }

        public long SizeBytes { get; set; }

        [Required]
        [MaxLength(400)]
        public required string StoredPath { get; set; }

        [MaxLength(450)]
        public string? UploadedByUserId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<ContentImageVariant> Variants { get; set; } = new List<ContentImageVariant>();
    }
}
