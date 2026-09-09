namespace altinnendata_api.Features.Admin
{
    public class AdminStatsDto
    {
        public int TotalUsers { get; set; }
        public int PublishedBuilds { get; set; }
        public int DraftBuilds { get; set; }
        public int SoldBuilds { get; set; }
        public int ReservedBuilds { get; set; }
        public int AvailableBuilds { get; set; }

        /// <summary>Summed price of the builds marked Sold. Builds without a price are left out and counted in <see cref="SoldWithoutPrice"/>.</summary>
        public long RevenueNok { get; set; }
        public int SoldWithoutPrice { get; set; }

        /// <summary>Sold builds with no sale date yet, so they are missing from the sold line in the history chart.</summary>
        public int SoldWithoutDate { get; set; }

        public int CatalogParts { get; set; }
        public int ContentImages { get; set; }
        public long StorageUsedBytes { get; set; }
        public long DiskTotalBytes { get; set; }
        public long DiskFreeBytes { get; set; }
    }

    public class DailyStatDto
    {
        public string Date { get; set; } = string.Empty;
        public int TotalUsers { get; set; }
        public int PublishedBuilds { get; set; }
        public int DraftBuilds { get; set; }
        public int SoldBuilds { get; set; }
        public int CatalogParts { get; set; }
        public int ContentImages { get; set; }
    }

    public class EmailStatsDto
    {
        public int Days { get; set; }
        public int Requests { get; set; }
        public int Delivered { get; set; }
        public int HardBounces { get; set; }
        public int SoftBounces { get; set; }
        public int SpamReports { get; set; }
        public int Blocked { get; set; }
        public int Invalid { get; set; }
    }
}
