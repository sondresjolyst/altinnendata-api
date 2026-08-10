using System.ComponentModel.DataAnnotations;

namespace altinnendata_api.Models
{
    /// <summary>A generated webp rendition of a <see cref="ContentImage"/> at a specific width, used for serving.</summary>
    public class ContentImageVariant
    {
        [Key]
        [MaxLength(32)]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [Required]
        [MaxLength(32)]
        public required string ContentImageId { get; set; }

        public ContentImage? ContentImage { get; set; }

        public int Width { get; set; }

        public long SizeBytes { get; set; }

        [Required]
        [MaxLength(400)]
        public required string StoredPath { get; set; }

        public static ContentImageVariant From(string contentImageId, (int Width, string StoredPath, long SizeBytes) v) =>
            new() { ContentImageId = contentImageId, Width = v.Width, StoredPath = v.StoredPath, SizeBytes = v.SizeBytes };
    }
}
