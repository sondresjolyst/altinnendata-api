using System.ComponentModel.DataAnnotations;

namespace altinnendata_api.Models.Content
{
    /// <summary>Home page sections for one locale, stored as a JSON array blob.</summary>
    public class HomePageContent
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(10)]
        public required string Locale { get; set; }

        [Required]
        public string SectionsJson { get; set; } = "[]";

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
