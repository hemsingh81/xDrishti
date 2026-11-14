using XDrishti.Application.Replay.Sim;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Replay.Sim;

/// <summary>SR-04: the cursor steps over the trading calendar, skipping weekends and holidays, and stops at the end.</summary>
public sealed class SimDaysSupportTests
{
    private static readonly TradingCalendar Calendar = new([TradingDay.Holiday(new DateOnly(2026, 11, 10), CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch)]);

    [Theory]
    [InlineData("2026-11-06", "2026-11-09")] // Friday to Monday
    [InlineData("2026-11-09", "2026-11-11")] // over the holiday
    public void Next_skips_weekends_and_holidays(string day, string next) =>
        SimDaysSupport.Next(Calendar, DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture), new DateOnly(2026, 12, 31)).ShouldBe(DateOnly.Parse(next, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void There_is_no_next_session_past_the_end_and_the_first_is_on_or_after_the_start()
    {
        SimDaysSupport.Next(Calendar, new DateOnly(2026, 11, 13), new DateOnly(2026, 11, 15)).ShouldBeNull();
        SimDaysSupport.FirstOnOrAfter(Calendar, new DateOnly(2026, 11, 7), new DateOnly(2026, 11, 30)).ShouldBe(new DateOnly(2026, 11, 9));
        SimDaysSupport.Count(Calendar, new DateOnly(2026, 11, 9), new DateOnly(2026, 11, 13)).ShouldBe(4);
    }
}
