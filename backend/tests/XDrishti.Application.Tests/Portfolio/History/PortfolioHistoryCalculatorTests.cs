using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Returns;
using XDrishti.Domain.Market.Actions;
using static XDrishti.Application.Tests.Portfolio.History.HistoryData;

namespace XDrishti.Application.Tests.Portfolio.History;

/// <summary>The value, invested, NAV and coverage of the history from lots and closes (acceptance criteria 1, 2 and 4 of P2-07).</summary>
public sealed class PortfolioHistoryCalculatorTests
{
    private static readonly DailyClose[] Alphas = Closes((5, 100m), (6, 110m), (7, 120m), (8, 90m), (9, 100m));

    private static PortfolioSeries Build(HistoryInput input) => PortfolioHistoryCalculator.Build(input);

    private static PortfolioSeries OneInstrument(DailyClose[] closes, params LotData[] lots) =>
        Build(Input([Holding(Alpha, lots)], By((Alpha, closes))));

    [Fact]
    public void Value_and_invested_follow_the_lots_held_on_each_day()
    {
        var series = OneInstrument(Alphas, Lot(10, 100m, 5), Lot(5, 110m, 7));

        series.Points.Select(p => (p.Date.Day, p.Value, p.Invested)).ShouldBe(
        [
            (5, 1000m, 1000m),
            (6, 1100m, 1000m),
            (7, 1800m, 1550m), // the second lot is held from its acquisition day: 15 x 120
            (8, 1350m, 1550m),
            (9, 1500m, 1550m),
        ]);
    }

    [Fact]
    public void A_lot_is_held_until_the_day_before_it_is_closed()
    {
        var series = OneInstrument(Alphas, Lot(10, 100m, 5), Sold(10, 100m, 5, 8, 95m));

        series.Points.Select(p => (p.Date.Day, p.Value, p.Invested)).ShouldBe(
        [
            (5, 2000m, 2000m),
            (6, 2200m, 2000m),
            (7, 2400m, 2000m),
            (8, 900m, 1000m), // closed on the 8th: gone from that day's value and cost
            (9, 1000m, 1000m),
        ]);
    }

    [Theory]
    [InlineData(6, 6)] // bought and sold on the same trading day
    [InlineData(10, 11)] // bought on a Saturday and sold on the Sunday: no trading day between
    [InlineData(10, 12)] // bought on a Saturday, sold on the Monday it first could be held
    public void A_lot_bought_and_sold_before_a_trading_day_comes_never_counts(int acquiredDay, int closedDay)
    {
        var with = OneInstrument(Alphas, Lot(10, 100m, 5), Sold(7, 100m, acquiredDay, closedDay, 120m));
        var without = OneInstrument(Alphas, Lot(10, 100m, 5));

        with.Points.ShouldBe(without.Points);
    }

    [Fact]
    public void A_day_without_a_close_carries_the_last_earlier_close_forward()
    {
        var halted = Closes((5, 100m), (9, 130m)); // no bars on the 6th, 7th and 8th

        var series = OneInstrument(halted, Lot(10, 100m, 5));

        series.Points.Select(p => (p.Date.Day, p.Value)).ShouldBe([(5, 1000m), (6, 1000m), (7, 1000m), (8, 1000m), (9, 1300m)]);
    }

    [Fact]
    public void A_holiday_has_no_point_and_the_dates_follow_the_calendar()
    {
        var series = Build(Input([Holding(Alpha, Lot(10, 100m, 5))], By((Alpha, Alphas)), calendar: WithHoliday(Oct(7))));

        series.Points.Select(p => p.Date.Day).ShouldBe([5, 6, 8, 9]);
    }

    [Fact]
    public void A_lot_dated_on_a_weekend_is_held_from_the_next_trading_day()
    {
        var closes = Closes((9, 100m), (12, 105m), (13, 110m));

        var series = OneInstrument(closes, Lot(10, 100m, 10));

        series.Points.Select(p => (p.Date.Day, p.Value, p.Invested)).ShouldBe([(12, 1050m, 1000m), (13, 1100m, 1000m)]);
        series.Coverage.LateStart.ShouldBe(0); // the bar of the 9th is before the lot: nothing is missing
    }

    [Fact]
    public void Editing_the_acquisition_date_of_a_lot_moves_where_its_value_starts()
    {
        var before = OneInstrument(Alphas, Lot(10, 100m, 5));
        var after = OneInstrument(Alphas, Lot(10, 100m, 7));

        before.Points[0].Date.ShouldBe(Oct(5));
        after.Points[0].Date.ShouldBe(Oct(7));
        after.Points.Select(p => (p.Date.Day, p.Value)).ShouldBe([(7, 1200m), (8, 900m), (9, 1000m)]);
    }

    [Fact]
    public void A_split_confirmed_on_the_instrument_keeps_the_value_continuous_across_the_ex_date()
    {
        // A 1:2 split on the 7th: the shares the broker shows now are post-split (20 at 100), the stored closes of earlier days are raw (200, 210).
        var raw = new[] { new StoredClose(Oct(5), 200.0), new StoredClose(Oct(6), 210.0), new StoredClose(Oct(7), 105.0), new StoredClose(Oct(8), 110.0) };
        var split = new AppliedAction(Oct(7), CorporateActionKind.Split, 1, 2, 0.5m);
        var adjusted = AdjustedCloses.Adjust(raw, AdjustmentFactors.From([split]));

        var series = OneInstrument([.. adjusted], Lot(20, 100m, 5));

        series.Points.Select(p => (p.Date.Day, p.Value)).ShouldBe([(5, 2000m), (6, 2100m), (7, 2100m), (8, 2200m)]);
        series.Points.Select(p => p.Nav).ShouldBe([100m, 105m, 105m, 110m]);
    }

    [Fact]
    public void Without_the_adjustment_the_same_lot_would_show_a_false_fall_at_the_ex_date()
    {
        var series = OneInstrument(Closes((5, 200m), (6, 210m), (7, 105m), (8, 110m)), Lot(20, 100m, 5));

        DrawdownCalculator.OfPortfolio(series.Points)!.Pct.ShouldBe(50m);
    }

    [Fact]
    public void A_holding_without_any_close_is_left_out_and_counted_never_valued_at_zero()
    {
        var series = Build(Input(
            [Holding(Alpha, Lot(10, 100m, 5)), Holding(Beta, Lot(100, 50m, 5)), Holding(null, Lot(1, 10m, 5))],
            By((Alpha, Alphas))));

        series.Coverage.ShouldBe(new HistoryCoverage(3, 1, 2, 0));
        series.Partial.ShouldBeTrue();
        series.Points.Select(p => (p.Date.Day, p.Value, p.Invested)).First().ShouldBe((5, 1000m, 1000m));
    }

    [Fact]
    public void A_closed_holding_without_closes_makes_the_series_partial_but_is_not_a_missing_open_holding()
    {
        var closed = HoldingFixtures.Holding(100m, "OLD", "closed", Beta, Sold(10, 100m, 5, 7, 110m));

        var series = Build(Input([Holding(Alpha, Lot(10, 100m, 5)), closed], By((Alpha, Alphas))));

        series.Coverage.ShouldBe(new HistoryCoverage(1, 1, 0, 0));
        series.Partial.ShouldBeTrue();
    }

    [Fact]
    public void Every_holding_is_counted_even_when_two_hold_the_same_instrument()
    {
        var series = Build(Input([Holding(Alpha, Lot(10, 100m, 5)), Holding(Alpha, Lot(4, 100m, 6))], By((Alpha, Alphas))));

        series.Coverage.ShouldBe(new HistoryCoverage(2, 2, 0, 0));
        series.Partial.ShouldBeFalse();
        series.Points.Select(p => p.Value).ShouldBe([1000m, 1540m, 1680m, 1260m, 1400m]);
    }

    [Fact]
    public void A_lot_older_than_the_first_close_counts_from_that_close_and_the_history_says_so()
    {
        var closes = Closes((7, 120m), (8, 90m), (9, 100m));

        var series = OneInstrument(closes, Lot(10, 100m, 5), Lot(5, 110m, 8));

        series.Points.Select(p => (p.Date.Day, p.Value, p.Invested)).ShouldBe([(7, 1200m, 1000m), (8, 1350m, 1550m), (9, 1500m, 1550m)]);
        series.Coverage.ShouldBe(new HistoryCoverage(1, 1, 0, 1));
        series.Partial.ShouldBeTrue();
    }

    [Fact]
    public void A_lot_that_was_closed_before_the_first_close_adds_nothing()
    {
        var closes = Closes((8, 90m), (9, 100m));

        var series = OneInstrument(closes, Lot(10, 100m, 8), Sold(10, 100m, 5, 7, 110m));

        series.Points.Select(p => p.Invested).ShouldBe([1000m, 1000m]);
        series.Coverage.LateStart.ShouldBe(1);
    }

    [Fact]
    public void Two_instruments_add_up_each_valued_at_its_own_close()
    {
        var series = Build(Input(
            [Holding(Alpha, Lot(10, 100m, 5)), Holding(Beta, Lot(2, 500m, 6))],
            By((Alpha, Alphas), (Beta, Closes((5, 500m), (6, 520m), (7, 480m), (8, 500m), (9, 510m))))));

        series.Points.Select(p => (p.Date.Day, p.Value, p.Invested)).ShouldBe(
        [
            (5, 1000m, 1000m),
            (6, 2140m, 2000m),
            (7, 2160m, 2000m),
            (8, 1900m, 2000m),
            (9, 2020m, 2000m),
        ]);
    }

    [Fact]
    public void No_lots_give_no_points()
    {
        Build(Input([], By())).Points.ShouldBeEmpty();
        Build(Input([Holding(Alpha, Lot(10, 100m, 5))], By())).Points.ShouldBeEmpty();
    }

    [Fact]
    public void Today_s_live_point_is_added_at_the_current_price_on_a_later_trading_day()
    {
        var live = new LivePrices(Oct(12), new Dictionary<Guid, decimal> { [Alpha] = 130m }, new DateTimeOffset(2026, 10, 12, 5, 5, 0, TimeSpan.Zero));

        var series = Build(Input([Holding(Alpha, Lot(10, 100m, 5))], By((Alpha, Alphas)), live: live));

        series.Points.Select(p => (p.Date, p.Value)).Last().ShouldBe((Oct(12), 1300m));
        series.Points.Count.ShouldBe(6);
        series.LastPriceAt.ShouldBe(live.At);
    }

    [Fact]
    public void A_live_point_on_a_day_with_a_stored_close_replaces_that_close()
    {
        var live = new LivePrices(Oct(9), new Dictionary<Guid, decimal> { [Alpha] = 105m }, new DateTimeOffset(2026, 10, 9, 9, 0, 0, TimeSpan.Zero));

        var series = Build(Input([Holding(Alpha, Lot(10, 100m, 5))], By((Alpha, Alphas)), live: live));

        series.Points.Count.ShouldBe(5);
        series.Points[^1].Value.ShouldBe(1050m);
    }

    [Fact]
    public void A_live_point_on_a_day_that_is_not_a_trading_day_is_not_added()
    {
        var live = new LivePrices(Oct(10), new Dictionary<Guid, decimal> { [Alpha] = 130m }, new DateTimeOffset(2026, 10, 10, 5, 0, 0, TimeSpan.Zero));

        var series = Build(Input([Holding(Alpha, Lot(10, 100m, 5))], By((Alpha, Alphas)), live: live));

        series.Points.Count.ShouldBe(5);
        series.LastPriceAt.ShouldBeNull();
    }

    [Fact]
    public void A_live_price_of_another_instrument_leaves_the_stored_close_in_use()
    {
        var live = new LivePrices(Oct(12), new Dictionary<Guid, decimal> { [Beta] = 999m }, new DateTimeOffset(2026, 10, 12, 5, 5, 0, TimeSpan.Zero));

        var series = Build(Input([Holding(Alpha, Lot(10, 100m, 5))], By((Alpha, Alphas)), live: live));

        series.Points[^1].Value.ShouldBe(1000m); // carried forward from the 9th
    }

    [Fact]
    public void The_nav_starts_at_100_and_a_purchase_does_not_move_it()
    {
        var series = OneInstrument(Closes((5, 100m), (6, 100m), (7, 100m), (8, 100m), (9, 100m)), Lot(10, 100m, 5), Lot(30, 100m, 7));

        series.Points.Select(p => p.Nav).ShouldBe([100m, 100m, 100m, 100m, 100m]);
        series.Points.Select(p => p.Value).ShouldBe([1000m, 1000m, 4000m, 4000m, 4000m]);
    }

    [Fact]
    public void A_sale_does_not_move_the_nav_either()
    {
        var series = OneInstrument(Alphas, Lot(10, 100m, 5), Sold(10, 100m, 5, 8, 95m));

        series.Points.Select(p => p.Nav).ShouldBe([100m, 110m, 120m, 90m, 100m]);
    }

    [Fact]
    public void A_deposit_during_a_drawdown_does_not_hide_it_nor_deepen_it()
    {
        // The price falls 20 %, the owner buys as much again at the low, the price recovers by a quarter: the value series only rises, the NAV shows the real 20 % fall.
        var closes = Closes((5, 100m), (6, 80m), (7, 100m));

        var series = OneInstrument(closes, Lot(10, 100m, 5), Lot(10, 80m, 6));

        series.Points.Select(p => p.Value).ShouldBe([1000m, 1600m, 2000m]);
        series.Points.Select(p => p.Nav).ShouldBe([100m, 80m, 100m]);
        DrawdownCalculator.OfPortfolio(series.Points).ShouldBe(new Drawdown(20m, Oct(5), Oct(6)));
    }

    [Fact]
    public void Selling_everything_and_buying_again_continues_the_nav_instead_of_restarting_it()
    {
        var closes = Closes((5, 100m), (6, 120m), (7, 120m), (8, 60m), (9, 66m));

        var series = OneInstrument(closes, Sold(10, 100m, 5, 7, 120m), Lot(10, 60m, 8));

        series.Points.Select(p => p.Nav).ShouldBe([100m, 120m, 120m, 120m, 132m]);
        series.Points.Select(p => p.Value).ShouldBe([1000m, 1200m, 0m, 600m, 660m]);
    }

    [Fact]
    public void Without_a_benchmark_the_benchmark_figures_are_empty_and_say_why()
    {
        var series = OneInstrument(Alphas, Lot(10, 100m, 5));

        series.Benchmark.ShouldBe(BenchmarkAvailability.NoBars);
        series.Points.ShouldAllBe(p => p.BenchmarkValue == null && p.BenchmarkNav == null);
    }

    [Fact]
    public void A_benchmark_that_starts_after_the_first_flow_is_unavailable_not_zero()
    {
        var series = Build(Input([Holding(Alpha, Lot(10, 100m, 5))], By((Alpha, Alphas)), benchmark: Closes((6, 1000m), (7, 1100m))));

        series.Benchmark.ShouldBe(BenchmarkAvailability.StartsAfterFirstFlow);
        series.Points.ShouldAllBe(p => p.BenchmarkValue == null);
    }

    [Fact]
    public void The_benchmark_receives_the_same_cash_flows_in_index_units()
    {
        var index = Closes((5, 1000m), (6, 1100m), (7, 1200m), (8, 900m), (9, 1000m));

        var series = Build(Input([Holding(Alpha, Lot(10, 100m, 5), Lot(5, 110m, 7))], By((Alpha, Alphas)), benchmark: index));

        // 1,000 buys 1 unit on the 5th; 550 buys 0.458333.. units at 1200 on the 7th
        series.Benchmark.ShouldBe(BenchmarkAvailability.Available);
        series.Points.Select(p => p.BenchmarkValue).ShouldBe([1000m, 1100m, 1750m, 1312.5m, 1458.3333m]);
        series.Points.Select(p => p.BenchmarkNav).ShouldBe([100m, 110m, 120m, 90m, 100m]); // the index itself, rebased: flows do not move it
    }

    [Fact]
    public void The_benchmark_never_sells_units_it_does_not_hold()
    {
        // 10 shares bought for 1,000 on the 5th (1 index unit at 1,000) are sold on the 8th for 1,500 (1.67 units at 900): the benchmark can only give back its one unit.
        var index = Closes((5, 1000m), (8, 900m));

        var series = Build(Input([Holding(Alpha, Sold(10, 100m, 5, 8, 150m))], By((Alpha, Alphas)), benchmark: index));

        series.Points.Select(p => p.BenchmarkValue).ShouldBe(new decimal?[] { 1000m, 1000m, 1000m, 0m, null }); // the index ends on the 8th
        series.Points.Select(p => p.Value).ShouldBe([1000m, 1100m, 1200m, 0m, 0m]);
    }

    [Fact]
    public void The_benchmark_carries_its_close_over_a_day_without_a_bar_but_ends_at_its_last_close()
    {
        var index = Closes((5, 1000m), (8, 900m)); // no bar on the 6th and the 7th, none after the 8th

        var series = Build(Input([Holding(Alpha, Lot(10, 100m, 5))], By((Alpha, Alphas)), benchmark: index));

        series.Points.Select(p => p.BenchmarkValue).ShouldBe(new decimal?[] { 1000m, 1000m, 1000m, 900m, null });
        series.Points.Select(p => p.BenchmarkNav).ShouldBe(new decimal?[] { 100m, 100m, 100m, 90m, null });
        series.BenchmarkAsOf.ShouldBe(Oct(8));
    }

    [Fact]
    public void A_benchmark_that_ends_before_the_first_flow_is_unavailable()
    {
        var series = Build(Input([Holding(Alpha, Lot(10, 100m, 7))], By((Alpha, Alphas)), benchmark: Closes((5, 1000m), (6, 1000m))));

        series.Benchmark.ShouldBe(BenchmarkAvailability.EndsBeforeFirstFlow);
        series.BenchmarkAsOf.ShouldBeNull();
        series.Points.ShouldAllBe(p => p.BenchmarkValue == null);
    }

    [Fact]
    public void A_live_day_has_no_benchmark_value_and_the_as_of_is_the_last_close()
    {
        var live = new LivePrices(Oct(12), new Dictionary<Guid, decimal> { [Alpha] = 130m }, new DateTimeOffset(2026, 10, 12, 5, 5, 0, TimeSpan.Zero));

        var series = Build(Input([Holding(Alpha, Lot(10, 100m, 5))], By((Alpha, Alphas)), benchmark: Closes((5, 1000m), (9, 1000m)), live: live));

        series.Points[^1].Date.ShouldBe(Oct(12));
        (series.Points[^1].BenchmarkValue, series.Points[^1].BenchmarkNav).ShouldBe((null, null));
        series.Points[^2].BenchmarkValue.ShouldBe(1000m);
        series.BenchmarkAsOf.ShouldBe(Oct(9));
    }

    [Fact]
    public void A_live_price_for_an_instrument_that_is_not_in_the_history_makes_no_point_and_no_latest_price_time()
    {
        var live = new LivePrices(Oct(12), new Dictionary<Guid, decimal> { [Beta] = 55m }, new DateTimeOffset(2026, 10, 12, 5, 5, 0, TimeSpan.Zero));

        var series = Build(Input([Holding(Alpha, Lot(10, 100m, 5)), Holding(Beta, Lot(1, 50m, 5))], By((Alpha, Alphas)), live: live));

        series.Points[^1].Date.ShouldBe(Oct(9));
        series.LastPriceAt.ShouldBeNull();
    }

    [Fact]
    public void The_chart_s_final_benchmark_value_is_what_the_xirr_flows_end_in_for_weekend_and_same_day_lots()
    {
        // the units of both are bought by one rule: a Saturday lot at Monday's close, a lot bought and sold before a trading day leaves nothing
        var index = Closes((5, 1000m), (6, 1100m), (7, 1200m), (8, 900m), (9, 1000m), (12, 1050m), (13, 1100m));
        var stock = Closes((5, 100m), (6, 110m), (7, 120m), (8, 90m), (9, 100m), (12, 105m), (13, 110m));
        var holdings = new[]
        {
            Holding(Alpha, Lot(10, 100m, 5), Lot(2, 150m, 10), Sold(3, 90m, 10, 11, 95m), Sold(4, 80m, 6, 8, 120m)),
        };

        var series = Build(Input(holdings, By((Alpha, stock)), benchmark: index));
        var xirr = BenchmarkReturns.Flows(holdings, Oct(13), index, Weekdays);

        series.Points[^1].BenchmarkValue.ShouldBe(Math.Round(xirr.ClosingValue, 4));
        xirr.Flows.Count(f => f.Amount < 0m).ShouldBe(3); // the Saturday-Sunday lot is gone
        xirr.Flows.Where(f => f.Amount < 0m).Select(f => f.Date.Day).Order().ShouldBe([5, 6, 10]); // the flows keep their own dates
    }
}
