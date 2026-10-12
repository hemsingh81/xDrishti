using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Dhan;
using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Application.Tests.Market.Bars;

public sealed class DhanFetchPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 6, 30, 0, TimeSpan.Zero);

    [Fact]
    public void An_instrument_with_bars_is_fetched_from_two_days_before_its_last_bar_to_now_in_one_window()
    {
        var last = Now.AddDays(-5);

        var windows = DhanFetchPlanner.Windows(last, Now, 365);

        windows.ShouldBe([new FetchWindow(last.AddDays(-2), Now)]);
    }

    [Fact]
    public void An_instrument_without_bars_goes_the_initial_days_back_in_windows_of_at_most_90_days_that_meet_at_their_boundaries()
    {
        var windows = DhanFetchPlanner.Windows(null, Now, 365);

        windows.Count.ShouldBe(5);
        windows[0].From.ShouldBe(Now.AddDays(-365));
        windows[^1].To.ShouldBe(Now);
        windows.ShouldAllBe(w => w.To - w.From <= TimeSpan.FromDays(90));
        windows.Zip(windows.Skip(1)).ShouldAllBe(pair => pair.First.To == pair.Second.From);
        windows.Take(4).ShouldAllBe(w => w.To - w.From == TimeSpan.FromDays(90));
        windows[4].To.ShouldBe(Now);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(90, 1)]
    [InlineData(91, 2)]
    [InlineData(180, 2)]
    [InlineData(181, 3)]
    [InlineData(3650, 41)]
    public void The_number_of_windows_follows_the_90_day_limit(int days, int count)
    {
        DhanFetchPlanner.Windows(null, Now, days).Count.ShouldBe(count);
    }

    [Fact]
    public void A_last_bar_in_the_future_or_a_zero_setting_still_gives_a_sane_plan()
    {
        var future = DhanFetchPlanner.Windows(Now.AddDays(3), Now, 365);
        var zero = DhanFetchPlanner.Windows(null, Now, 0);

        future.ShouldBe([new FetchWindow(Now.AddDays(-2), Now)]);
        zero.ShouldBe([new FetchWindow(Now.AddDays(-1), Now)]);
    }

    [Fact]
    public void A_very_old_last_bar_is_split_like_a_missing_one()
    {
        DhanFetchPlanner.Windows(Now.AddDays(-200), Now, 365).Count.ShouldBe(3); // 202 days
    }

    [Fact]
    public void The_plan_has_one_entry_per_target_with_its_own_windows()
    {
        var a = new FetchTarget(Guid.NewGuid(), "NSE:ABB", "share", "13", "NSE_EQ", Now.AddDays(-1));
        var b = new FetchTarget(Guid.NewGuid(), "NSE:NIFTY", "index", "13", "IDX_I", null);

        var plan = DhanFetchPlanner.Plan([a, b], Now, 100);

        plan.Targets.Select(t => (t.InstrumentId, t.Symbol, t.Type, t.SecurityId, t.Segment)).ShouldBe(
            [(a.InstrumentId, "NSE:ABB", "share", "13", "NSE_EQ"), (b.InstrumentId, "NSE:NIFTY", "index", "13", "IDX_I")]);
        plan.Targets[0].Windows.Count.ShouldBe(1);
        plan.Targets[1].Windows.Count.ShouldBe(2);
    }

    private static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    private static DateTimeOffset Midnight(int month, int day) => new(2026, month, day, 0, 0, 0, Ist);

    [Fact]
    public void Consecutive_gap_sessions_share_one_window_from_the_first_midnight_to_the_midnight_after_the_last()
    {
        var days = new[] { new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6), new DateOnly(2026, 10, 7) };

        var windows = DhanFetchPlanner.GapWindows(days, new TradingCalendar(), Now);

        windows.ShouldBe([new FetchWindow(Midnight(10, 5), Midnight(10, 8))]);
    }

    [Fact]
    public void A_weekend_between_two_gap_sessions_does_not_split_the_window()
    {
        var days = new[] { new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 12) };

        var windows = DhanFetchPlanner.GapWindows(days, new TradingCalendar(), Now.AddDays(3));

        windows.ShouldBe([new FetchWindow(Midnight(10, 8), Midnight(10, 13))]);
    }

    [Fact]
    public void A_session_that_is_not_gap_between_two_gap_sessions_splits_the_windows()
    {
        var days = new[] { new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 7) }; // Tuesday the 6th is complete

        var windows = DhanFetchPlanner.GapWindows(days, new TradingCalendar(), Now);

        windows.ShouldBe([new FetchWindow(Midnight(10, 5), Midnight(10, 6)), new FetchWindow(Midnight(10, 7), Midnight(10, 8))]);
    }

    [Fact]
    public void A_holiday_between_two_gap_sessions_does_not_split_the_window()
    {
        var holiday = TradingDay.Holiday(new DateOnly(2026, 10, 6), CalendarSource.Manual, CalendarStatus.Confirmed, null, Now);
        var days = new[] { new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 7) };

        var windows = DhanFetchPlanner.GapWindows(days, new TradingCalendar([holiday]), Now);

        windows.ShouldBe([new FetchWindow(Midnight(10, 5), Midnight(10, 8))]);
    }

    [Fact]
    public void A_run_of_gap_sessions_longer_than_90_days_is_cut_into_windows_of_at_most_90_days()
    {
        var days = Enumerable.Range(0, 200).Select(i => new DateOnly(2026, 1, 1).AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();

        var windows = DhanFetchPlanner.GapWindows(days, new TradingCalendar(), Now);

        windows.Count.ShouldBe(3);
        windows.ShouldAllBe(w => w.To - w.From <= TimeSpan.FromDays(90));
        windows[0].From.ShouldBe(Midnight(1, 1));
        windows.Zip(windows.Skip(1)).ShouldAllBe(pair => pair.First.To == pair.Second.From);
    }

    [Fact]
    public void A_gap_window_never_reaches_beyond_now_and_no_gap_sessions_make_no_windows()
    {
        var today = new[] { new DateOnly(2026, 10, 12) };

        DhanFetchPlanner.GapWindows(today, new TradingCalendar(), Now).ShouldBe([new FetchWindow(Midnight(10, 12), Now)]);
        DhanFetchPlanner.GapWindows([], new TradingCalendar(), Now).ShouldBeEmpty();
    }

    [Fact]
    public void The_plan_asks_for_the_gap_windows_before_the_ordinary_ones_and_leaves_other_targets_alone()
    {
        var a = new FetchTarget(Guid.NewGuid(), "NSE:ABB", "share", "13", "NSE_EQ", Now.AddDays(-1));
        var b = new FetchTarget(Guid.NewGuid(), "NSE:TCS", "share", "14", "NSE_EQ", Now.AddDays(-1));
        var gap = new FetchWindow(Midnight(9, 1), Midnight(9, 2));

        var plan = DhanFetchPlanner.Plan([a, b], Now, 100, new Dictionary<Guid, IReadOnlyList<FetchWindow>> { [a.InstrumentId] = [gap] });

        plan.Targets[0].Windows.ShouldBe([gap, new FetchWindow(Now.AddDays(-3), Now)]);
        plan.Targets[1].Windows.ShouldBe([new FetchWindow(Now.AddDays(-3), Now)]);
    }

    [Fact]
    public void The_ordinary_fetch_starts_where_the_windows_say()
    {
        DhanFetchPlanner.StartOf(Now.AddDays(-5), Now, 365).ShouldBe(Now.AddDays(-7));
        DhanFetchPlanner.StartOf(null, Now, 30).ShouldBe(Now.AddDays(-30));
        DhanFetchPlanner.StartOf(Now.AddDays(3), Now, 30).ShouldBe(Now.AddDays(-2));
    }
}
