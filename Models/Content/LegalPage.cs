using System.ComponentModel.DataAnnotations;

namespace altinnendata_api.Models.Content
{
    public static class LegalPageKeys
    {
        public const string Terms = "terms";
        public const string Privacy = "privacy";
        public const string Cookies = "cookies";

        public static readonly string[] All = [Terms, Privacy, Cookies];

        public static bool IsValid(string? key) => key != null && All.Contains(key, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Body text of a legal page for one locale, edited by an admin and rendered as markdown.</summary>
    public class LegalPage
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(40)]
        public required string Key { get; set; }

        [Required]
        [MaxLength(10)]
        public required string Locale { get; set; }

        [Required]
        [MaxLength(200)]
        public required string Title { get; set; }

        [Required]
        public string BodyMarkdown { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
