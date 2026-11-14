using System.Runtime.Versioning;
using XDrishti.Api.Simulate.Assist;
using XDrishti.SimReader;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>P3-09e: the grouping of a range of stored minutes into one session per India date.</summary>
[UnsupportedOSPlatform("windows")]
public sealed class StoredDaysTests
{
    private static ReaderBar At(string utc) => new(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture).ToUnixTimeSeconds(), 1m, 1m, 1m, 1m, 1m);

    [Fact]
    public void Minutes_are_grouped_by_their_India_date_not_the_UTC_date_and_the_sessions_ascend()
    {
        // 2026-03-02 09:15 IST = 03:45 UTC; 15:29 IST = 09:59 UTC; 2026-03-01 18:30 UTC is 2026-03-02 00:00 IST; 2026-03-03 09:15 IST = 03:45 UTC
        var bars = new[] { At("2026-03-01T18:30:00Z"), At("2026-03-02T03:45:00Z"), At("2026-03-02T09:59:00Z"), At("2026-03-03T03:45:00Z") };

        var sessions = StoredDays.Sessions(bars);

        sessions.Select(s => s.Date).ShouldBe([new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 3)]);
        sessions[0].Bars.Count.ShouldBe(3);
        sessions[1].Bars.Count.ShouldBe(1);
    }

    [Fact]
    public void No_minutes_give_no_session()
    {
        StoredDays.Sessions([]).ShouldBeEmpty();
    }
}
