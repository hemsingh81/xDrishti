using XDrishti.Application.Market.Bars.Gaps;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Market.Bars;

public sealed class GapSessionsTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static readonly DateTimeOffset Afternoon = new(2026, 10, 12, 15, 50, 0, Ist); // Monday, the session has closed

    private static readonly DateTimeOffset LongAgo = new(2025, 1, 1, 9, 15, 0, Ist);

    private static TradingDay Holiday(int month, int day) => TradingDay.Holiday(new DateOnly(2026, month, day), CalendarSource.Manual, CalendarStatus.Confirmed, null, Afternoon);

    [Fact]
    public void The_sessions_are_the_latest_closed_ones_newest_first_up_to_the_lookback()
    {
        var sessions = GapSessions.Recent(new TradingCalendar(), Afternoon, 5, LongAgo);

        sessions.ShouldBe([new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 6)]);
    }

    [Fact]
    public void A_session_that_is_still_open_or_in_the_future_is_never_examined()
    {
        var midday = new DateTimeOffset(2026, 10, 12, 12, 0, 0, Ist);

        var sessions = GapSessions.Recent(new TradingCalendar(), midday, 2, LongAgo);

        sessions.ShouldBe([new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 8)]);
    }

    [Fact]
    public void The_minute_before_the_close_is_still_open_and_the_minute_of_the_close_is_not()
    {
        GapSessions.Recent(new TradingCalendar(), new DateTimeOffset(2026, 10, 12, 15, 29, 0, Ist), 1, LongAgo).ShouldBe([new DateOnly(2026, 10, 9)]);
        GapSessions.Recent(new TradingCalendar(), new DateTimeOffset(2026, 10, 12, 15, 30, 0, Ist), 1, LongAgo).ShouldBe([new DateOnly(2026, 10, 12)]);
    }

    [Fact]
    public void A_holiday_is_not_a_session_and_the_lookback_counts_sessions_not_calendar_days()
    {
        var sessions = GapSessions.Recent(new TradingCalendar([Holiday(10, 8)]), Afternoon, 4, LongAgo);

        sessions.ShouldBe([new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 6)]);
    }

    [Fact]
    public void A_special_session_on_a_weekend_is_a_session()
    {
        var saturday = TradingDay.Special(new DateOnly(2026, 10, 10), 10 * 60, 11 * 60, CalendarSource.Manual, CalendarStatus.Confirmed, null, Afternoon);

        var sessions = GapSessions.Recent(new TradingCalendar([saturday]), Afternoon, 3, LongAgo);

        sessions.ShouldBe([new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 9)]);
    }

    [Fact]
    public void Only_sessions_after_the_day_of_the_first_bar_are_examined()
    {
        var firstBar = new DateTimeOffset(2026, 10, 7, 11, 0, 0, Ist);

        var sessions = GapSessions.Recent(new TradingCalendar(), Afternoon, 30, firstBar);

        sessions.ShouldBe([new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 8)]);
    }

    [Fact]
    public void An_instrument_without_bars_or_a_lookback_below_one_has_no_sessions()
    {
        GapSessions.Recent(new TradingCalendar(), Afternoon, 30, null).ShouldBeEmpty();
        GapSessions.Recent(new TradingCalendar(), Afternoon, 0, LongAgo).ShouldBeEmpty();
    }

    [Fact]
    public void A_first_bar_on_the_last_session_leaves_nothing_to_examine()
    {
        GapSessions.Recent(new TradingCalendar(), Afternoon, 30, new DateTimeOffset(2026, 10, 12, 9, 15, 0, Ist)).ShouldBeEmpty();
    }
}
