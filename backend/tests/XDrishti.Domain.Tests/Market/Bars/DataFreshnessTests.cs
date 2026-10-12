using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Tests.Market.Bars;

public sealed class DataFreshnessTests
{
    private static DateTimeOffset India(int year, int month, int day, int hour, int minute) => new(year, month, day, hour, minute, 0, TimeSpan.FromMinutes(330));

    [Theory]
    [InlineData(2026, 10, 12, 12, 0, 2026, 10, 9)]   // Monday during the session: Friday
    [InlineData(2026, 10, 12, 15, 29, 2026, 10, 9)]  // one minute before the close
    [InlineData(2026, 10, 12, 15, 30, 2026, 10, 12)] // Monday at the close: today
    [InlineData(2026, 10, 12, 23, 0, 2026, 10, 12)]
    [InlineData(2026, 10, 13, 0, 0, 2026, 10, 12)]   // Tuesday just after midnight: Monday
    [InlineData(2026, 10, 13, 8, 0, 2026, 10, 12)]
    [InlineData(2026, 10, 10, 12, 0, 2026, 10, 9)]   // Saturday: Friday
    [InlineData(2026, 10, 11, 20, 0, 2026, 10, 9)]   // Sunday: Friday
    [InlineData(2026, 10, 9, 16, 0, 2026, 10, 9)]    // Friday after the close: Friday
    public void The_last_closed_session_is_the_last_weekday_that_has_closed(int y, int m, int d, int hour, int minute, int ey, int em, int ed)
    {
        DataFreshness.LastClosedSession(India(y, m, d, hour, minute)).ShouldBe(new DateOnly(ey, em, ed));
    }

    [Fact]
    public void The_date_is_the_india_date_not_the_utc_date()
    {
        // 2026-10-12 21:00 UTC is Tuesday 02:30 in India.
        DataFreshness.LastClosedSession(new DateTimeOffset(2026, 10, 12, 21, 0, 0, TimeSpan.Zero)).ShouldBe(new DateOnly(2026, 10, 12));
    }

    [Theory]
    [InlineData(2026, 10, 9, 2026, 10, 9, 0)]
    [InlineData(2026, 10, 12, 2026, 10, 9, 0)]  // newer than expected: not behind
    [InlineData(2026, 10, 8, 2026, 10, 9, 1)]
    [InlineData(2026, 10, 7, 2026, 10, 9, 2)]
    [InlineData(2026, 10, 9, 2026, 10, 12, 1)]  // Friday's data on Monday evening: only Monday is missing, the weekend does not count
    [InlineData(2026, 10, 2, 2026, 10, 12, 6)]
    [InlineData(2026, 10, 10, 2026, 10, 12, 1)]  // a Saturday bar: Monday is missing
    public void The_sessions_behind_count_weekdays_only(int ly, int lm, int ld, int ey, int em, int ed, int behind)
    {
        DataFreshness.SessionsBehind(new DateOnly(ly, lm, ld), new DateOnly(ey, em, ed)).ShouldBe(behind);
    }
}
