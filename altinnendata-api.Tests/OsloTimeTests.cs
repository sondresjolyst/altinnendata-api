using altinnendata_api.Infrastructure;
using Xunit;

namespace altinnendata_api.Tests;

public class OsloTimeTests
{
    [Theory]
    [InlineData("2026-01-15T12:00:00", "2026-01-15T13:00:00")]
    [InlineData("2026-07-15T12:00:00", "2026-07-15T14:00:00")]
    [InlineData("2026-10-31T23:30:00", "2026-11-01T00:30:00")]
    [InlineData("2026-03-29T00:59:00", "2026-03-29T01:59:00")]
    [InlineData("2026-03-29T01:00:00", "2026-03-29T03:00:00")]
    public void FromUtc_GivesNorwegianWallClockTime(string utc, string oslo)
    {
        Assert.Equal(DateTime.Parse(oslo), OsloTime.FromUtc(DateTime.Parse(utc)));
    }

    [Fact]
    public void EnsureAvailable_FindsTheZoneHere() => OsloTime.EnsureAvailable();

    [Fact]
    public void Load_FallsBackToTheWindowsNameWhenTheIanaNameIsUnknown()
    {
        var oslo = TimeZoneInfo.CreateCustomTimeZone("test", TimeSpan.FromHours(1), "test", "test");
        var asked = new List<string>();

        var zone = OsloTime.Load(id =>
        {
            asked.Add(id);
            return id == OsloTime.WindowsZoneId ? oslo : throw new TimeZoneNotFoundException(id);
        });

        Assert.Same(oslo, zone);
        Assert.Equal([OsloTime.ZoneId, OsloTime.WindowsZoneId], asked);
    }

    [Fact]
    public void Load_StopsWithAClearErrorWhenTheZoneIsMissing()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => OsloTime.Load(id => throw new TimeZoneNotFoundException(id)));
        Assert.Contains("Europe/Oslo", ex.Message);
        Assert.Contains("tzdata", ex.Message);
    }
}
