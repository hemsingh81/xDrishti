using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Market.Quality;
using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.Tests.Reporting;

/// <summary>P8-11: the window of a replay on the trading calendar, worked by hand (March 2026: the 2nd is a Monday).</summary>
public sealed class TradeReplayWindowTests
{
    private static readonly TradingCalendar Calendar = new();

    private static long At(DateOnly date, int minute) => MinuteAggregator.MidnightOf(date) + (minute * 60L);

    [Fact]
    public void An_intraday_trade_shows_the_previous_session_from_its_open_to_the_close_of_its_exit_session()
    {
        var window = TradeReplayWindow.Compute(Calendar, new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 4), 1, 0);

        window.FromDate.ShouldBe(new DateOnly(2026, 3, 3));
        window.ToDate.ShouldBe(new DateOnly(2026, 3, 4));
        window.FromEpoch.ShouldBe(At(new DateOnly(2026, 3, 3), 9 * 60 + 15));
        window.ToEpoch.ShouldBe(At(new DateOnly(2026, 3, 4), 15 * 60 + 29));
    }

    [Fact]
    public void A_swing_trade_across_a_weekend_skips_the_non_sessions_on_both_sides()
    {
        var window = TradeReplayWindow.Compute(Calendar, new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 11), 1, 2);

        window.FromDate.ShouldBe(new DateOnly(2026, 3, 6));
        window.ToDate.ShouldBe(new DateOnly(2026, 3, 13));
    }

    [Fact]
    public void A_holiday_is_not_a_session()
    {
        var holiday = TradingDay.Holiday(new DateOnly(2026, 3, 3), CalendarSource.Manual, CalendarStatus.Confirmed, null, DateTimeOffset.UnixEpoch);
        var window = TradeReplayWindow.Compute(new TradingCalendar([holiday]), new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 4), 1, 0);

        window.FromDate.ShouldBe(new DateOnly(2026, 3, 2));
    }

    [Fact]
    public void After_extends_the_window_by_whole_sessions_from_the_exit_session()
    {
        var window = TradeReplayWindow.Compute(Calendar, new DateOnly(2026, 3, 4), new DateOnly(2026, 3, 4), 0, 3);

        window.FromDate.ShouldBe(new DateOnly(2026, 3, 4));
        window.ToDate.ShouldBe(new DateOnly(2026, 3, 9));
    }

    [Fact]
    public void The_day_timeframe_defaults_to_twenty_sessions_and_allows_sixty()
    {
        TradeReplayWindow.DefaultBefore(Timeframe.D1).ShouldBe(20);
        TradeReplayWindow.DefaultAfter(Timeframe.D1).ShouldBe(20);
        TradeReplayWindow.MaxSessions(Timeframe.D1).ShouldBe(60);
        TradeReplayWindow.DefaultBefore(Timeframe.M5).ShouldBe(1);
        TradeReplayWindow.MaxSessions(Timeframe.M5).ShouldBe(3);
    }

    [Theory]
    [InlineData(Timeframe.M1, true)]
    [InlineData(Timeframe.M5, true)]
    [InlineData(Timeframe.M15, true)]
    [InlineData(Timeframe.H1, true)]
    [InlineData(Timeframe.D1, true)]
    [InlineData(Timeframe.M10, false)]
    [InlineData(Timeframe.W1, false)]
    public void Only_five_timeframes_are_allowed(Timeframe timeframe, bool allowed) => TradeReplayWindow.IsAllowed(timeframe).ShouldBe(allowed);
}
