using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Application.Market.Bars.Dhan;

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
}
