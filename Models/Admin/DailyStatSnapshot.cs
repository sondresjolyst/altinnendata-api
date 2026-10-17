using System.ComponentModel.DataAnnotations;
using SjolystInnovation.AppKit.AdminStats;

namespace altinnendata_api.Models.Admin
{
    public class DailyStatSnapshot : IAppKitDailyStat
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
