using System.ComponentModel.DataAnnotations;

namespace altinnendata_api.Models.Admin
{
    public class DailyStatSnapshot
    {
        [Key]
        public int Id { get; set; }

        public DateOnly Date { get; set; }

        public int TotalUsers { get; set; }
        public int PublishedBuilds { get; set; }
        public int DraftBuilds { get; set; }
        public int CatalogParts { get; set; }
        public int ContentImages { get; set; }
    }
}
