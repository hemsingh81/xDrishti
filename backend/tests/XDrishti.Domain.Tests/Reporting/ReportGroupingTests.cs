using XDrishti.Domain.Reporting;
using static XDrishti.Domain.Tests.Reporting.ReportFixtures;

namespace XDrishti.Domain.Tests.Reporting;

/// <summary>Group-by on any subset of dimensions and the pivot of two (P8-05, ADR 0099).</summary>
public sealed class ReportGroupingTests
{
    private static ReportPopulation Mixed()
    {
        List<ReportTrade> trades =
        [
            Trade("r1", "T1", 1, strategy: "ORB", timeframe: "15m", side: "BUY", symbol: "AAA", baskets: ["A", "B"], day: 0),
            Trade("r1", "T2", -1, strategy: "ORB", timeframe: "15m", side: "BUY", symbol: "BBB", baskets: ["A"], day: 1),
            Trade("r1", "T3", 2, strategy: "ORB", timeframe: "5m", side: "SELL", symbol: "AAA", baskets: ["B"], day: 2),
            Trade("r1", "T4", 1, strategy: "VWAP", timeframe: "15m", side: "BUY", symbol: "CCC", baskets: [], day: 38),
        ];
        return Population([Run("r1"), Run("r2", exit: "TRAIL")], trades, Small(1));
    }

    [Fact]
    public void Every_dimension_can_be_grouped_alone_and_a_trade_is_in_one_group_unless_baskets_overlap()
    {
        var population = Mixed();

        Group(population, ReportDimension.Strategy).Groups.Select(g => (g.Key.Text, g.N)).ShouldBe([("Strategy=ORB", 3), ("Strategy=VWAP", 1)]);
        Group(population, ReportDimension.Timeframe).Groups.Select(g => (g.Key.Text, g.N)).ShouldBe([("Timeframe=15m", 3), ("Timeframe=5m", 1)]);
        Group(population, ReportDimension.Side).Groups.Select(g => (g.Key.Text, g.N)).ShouldBe([("Side=BUY", 3), ("Side=SELL", 1)]);
        Group(population, ReportDimension.Instrument).Groups.Select(g => (g.Key.Text, g.N)).ShouldBe([("Instrument=AAA", 2), ("Instrument=BBB", 1), ("Instrument=CCC", 1)]);
        Group(population, ReportDimension.Month).Groups.Select(g => (g.Key.Text, g.N)).ShouldBe([("Month=2026-01", 3), ("Month=2026-02", 1)]);
        Group(population, ReportDimension.Weekday).Groups.Select(g => (g.Key.Text, g.N)).ShouldBe([("Weekday=Mon", 1), ("Weekday=Tue", 1), ("Weekday=Wed", 1), ("Weekday=Thu", 1)]);
        Group(population, ReportDimension.Exit).Groups.Select(g => (g.Key.Text, g.N)).ShouldBe([("Exit=FIXED_RR", 4)]);
        Group(population, ReportDimension.Style).Groups.Select(g => (g.Key.Text, g.N)).ShouldBe([("Style=intraday", 4)]);
        Group(population, ReportDimension.Side).NonAdditive.ShouldBeFalse();
    }

    [Fact]
    public void An_empty_dimension_list_is_the_whole_population_as_one_group()
    {
        var grouping = Group(Mixed());

        grouping.Groups.Single().Key.Text.ShouldBe("all");
        grouping.Groups.Single().N.ShouldBe(4);
    }

    [Fact]
    public void Two_or_more_dimensions_make_the_cross_product_of_the_cells_that_have_trades_in_a_fixed_order()
    {
        var grouping = Group(Mixed(), ReportDimension.Strategy, ReportDimension.Timeframe, ReportDimension.Side);

        grouping.Groups.Select(g => (g.Key.Text, g.N)).ShouldBe(
        [
            ("Strategy=ORB|Timeframe=15m|Side=BUY", 2),
            ("Strategy=ORB|Timeframe=5m|Side=SELL", 1),
            ("Strategy=VWAP|Timeframe=15m|Side=BUY", 1),
        ]);
        grouping.Dimensions.ShouldBe([ReportDimension.Strategy, ReportDimension.Timeframe, ReportDimension.Side]);
    }

    [Fact]
    public void A_trade_in_two_baskets_is_in_both_and_the_grouping_says_the_totals_are_not_additive()
    {
        var grouping = Group(Mixed(), ReportDimension.Basket);

        grouping.NonAdditive.ShouldBeTrue();
        grouping.Groups.Select(g => (g.Key.Text, g.N)).ShouldBe([("Basket=A", 2), ("Basket=B", 2), ("Basket=(not recorded)", 1)]);
        grouping.Groups.Sum(g => g.N).ShouldBe(5);
        grouping.Groups[2].ExpectancyR.State.ShouldBe(MetricState.Unavailable);
        grouping.Groups[0].ExpectancyR.State.ShouldBe(MetricState.Value);
    }

    [Fact]
    public void A_dimension_may_not_repeat()
    {
        Mixed().GroupBy([ReportDimension.Side, ReportDimension.Side]).Error.Code.ShouldBe("reports.invalid");
    }

    [Fact]
    public void A_pivot_lays_out_rows_by_columns_with_empty_cells_masked_cells_and_values()
    {
        // ORB BUY 4 trades, ORB SELL 1, VWAP BUY 3, VWAP SELL none; the minimum is 3
        List<ReportTrade> trades =
        [
            .. Series("r1", "ORB", WinsAndLosses(3, 1), side: "BUY"),
            .. Series("r1", "ORB", [1], side: "SELL"),
            .. Series("r1", "VWAP", WinsAndLosses(1, 2), side: "BUY"),
        ];
        var population = Population([Run("r1")], trades, Small(3));

        var pivot = population.Pivot(ReportDimension.Strategy, ReportDimension.Side, ReportMetric.ExpectancyR).Value;

        pivot.Rows.ShouldBe(["ORB", "VWAP"]);
        pivot.Columns.ShouldBe(["BUY", "SELL"]);
        pivot.Cells.Count.ShouldBe(4);
        var orbBuy = pivot.Cells[0];
        orbBuy.N.ShouldBe(4);
        orbBuy.Value.State.ShouldBe(MetricState.Value);
        orbBuy.Value.Number!.Value.ShouldBe(0.5, 1e-12);
        var orbSell = pivot.Cells[1];
        orbSell.N.ShouldBe(1);
        orbSell.Value.State.ShouldBe(MetricState.Insufficient);
        orbSell.Value.Number.ShouldBeNull();
        pivot.Cells[2].Value.Number!.Value.ShouldBe(-1.0 / 3, 1e-12);
        var vwapSell = pivot.Cells[3];
        vwapSell.N.ShouldBe(0);
        vwapSell.Value.State.ShouldBe(MetricState.Empty);
        vwapSell.Value.Number.ShouldBeNull();
        pivot.Evidence.Population.N.ShouldBe(8);
    }

    [Fact]
    public void A_pivot_of_trade_counts_shows_a_count_for_a_thin_cell_but_empty_for_none()
    {
        List<ReportTrade> trades = [.. Series("r1", "ORB", WinsAndLosses(3, 1), side: "BUY"), .. Series("r1", "ORB", [1], side: "SELL")];
        var population = Population([Run("r1")], trades, Small(3));

        var pivot = population.Pivot(ReportDimension.Side, ReportDimension.Strategy, ReportMetric.Trades).Value;

        pivot.Cells.Select(c => c.Value.Number).ShouldBe(new double?[] { 4, 1 });
    }

    [Fact]
    public void A_pivot_needs_two_different_dimensions_and_refuses_unavailable_ones()
    {
        var population = Mixed();

        population.Pivot(ReportDimension.Side, ReportDimension.Side, ReportMetric.Trades).Error.Code.ShouldBe("reports.invalid");
        population.Pivot(ReportDimension.Side, ReportDimension.Regime, ReportMetric.Trades).Error.Code.ShouldBe("reports.dimension_unavailable");
    }

    [Fact]
    public void Weekdays_sort_in_calendar_order_not_alphabetically()
    {
        // 2026-01-05 is a Monday; days 1, 3 and 4 are Tue, Thu, Fri; the order of the weekday names is the week, not the alphabet
        List<ReportTrade> trades = [Trade("r1", "A", 1, day: 4), Trade("r1", "B", 1, day: 1), Trade("r1", "C", 1, day: 3)];

        var grouping = Group(Population([Run("r1")], trades, Small(1)), ReportDimension.Weekday);

        grouping.Groups.Select(g => g.Key.Text).ShouldBe(["Weekday=Tue", "Weekday=Thu", "Weekday=Fri"]);
    }
}
