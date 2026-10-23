namespace altinnendata_api.Infrastructure
{
    /// <summary>
    /// Norwegian local time for receipts, emails, documents and the VAT months. The zone comes from the container's
    /// time zone data, and a missing zone stops the app instead of quietly showing UTC.
    /// </summary>
    public static class OsloTime
    {
        public const string ZoneId = "Europe/Oslo";

        /// <summary>
        /// The same zone under its Windows name. With InvariantGlobalization, Windows cannot map the IANA name, so a
        /// local run on Windows finds the zone through this instead. Linux, where the app runs in production, uses the IANA name.
        /// </summary>
        public const string WindowsZoneId = "W. Europe Standard Time";

        private static readonly Lazy<TimeZoneInfo> LazyZone = new(() => Load(TimeZoneInfo.FindSystemTimeZoneById));

        public static TimeZoneInfo Zone => LazyZone.Value;

        /// <summary>Loads the zone at startup, so an image without time zone data fails the deploy and not a later email.</summary>
        public static void EnsureAvailable() => _ = Zone;

        /// <summary>The Oslo wall clock time of <paramref name="utc"/>.</summary>
        public static DateTime FromUtc(DateTime utc) =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);

        internal static TimeZoneInfo Load(Func<string, TimeZoneInfo> find)
        {
            try
            {
                return find(ZoneId);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                try
                {
                    return find(WindowsZoneId);
                }
                catch (Exception windows) when (windows is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    // Neither name is known, so the error below names the IANA zone the image needs.
                }

                throw new InvalidOperationException(
                    $"The {ZoneId} time zone is missing, so dates would be shown in UTC. Install the time zone data (tzdata) in the image.", ex);
            }
        }
    }
}
