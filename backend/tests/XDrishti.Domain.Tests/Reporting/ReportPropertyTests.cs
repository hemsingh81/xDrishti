using XDrishti.Domain.Learning;
using XDrishti.Domain.Reporting;
using static XDrishti.Domain.Tests.Reporting.ReportFixtures;

namespace XDrishti.Domain.Tests.Reporting;

/// <summary>Properties of the engine over seeded random populations: a trade is counted once, a non-value is never a number, the order of the input changes nothing, and a trade added to one group changes no other group's own figures (P8-05, ADR 0099).</summary>
public sealed class ReportPropertyTests
{
    private static readonly string[] Strategies = ["ORB", "VWAP", "GAP", "RSI"];
    private static readonly string[] Baskets = ["A", "B", "C"];
    private static readonly string[] Symbols = ["AAA", "BBB", "CCC", "DDD", "EEE"];

    private static readonly ReportDimension[][] Subsets =
    [
        [], [ReportDimension.Strategy], [ReportDimension.Side], [ReportDimension.Timeframe], [ReportDimension.Instrument], [ReportDimension.Month], [ReportDimension.Weekday],
        [ReportDimension.Strategy, ReportDimension.Side], [ReportDimension.Strategy, ReportDimension.Timeframe, ReportDimension.Side], [ReportDimension.Month, ReportDimension.Weekday],
        [ReportDimension.Basket], [ReportDimension.Basket, ReportDimension.Strategy], [ReportDimension.Exit, ReportDimension.Basket, ReportDimension.Side],
    ];

    private static (List<ReportRun> Runs, List<ReportTrade> Trades) Generate(int seed, int count = 120)
    {
        var random = new Random(seed);
        // one run per strategy: no overlap by construction; a quarter of the runs record no exit
        var runs = Strategies.Select((s, i) => Run("run-" + s, exit: i == 3 ? null : "FIXED_RR")).ToList();
        var trades = new List<ReportTrade>();
        for (var i = 0; i < count; i++)
        {
            var strategy = Strategies[random.Next(Strategies.Length)];
            var baskets = Baskets.Where(_ => random.Next(3) == 0).ToArray();
            var r = Math.Round((random.NextDouble() * 5) - 2, 2);
            trades.Add(Trade("run-" + strategy, $"{strategy}-{i:D4}", r, strategy, random.Next(2) == 0 ? "15m" : "5m", random.Next(2) == 0 ? "BUY" : "SELL", Symbols[random.Next(Symbols.Length)], baskets, random.Next(0, 80), random.Next(1, 5)));
        }

        return (runs, trades);
    }

    private static ReportPopulation Build(IReadOnlyList<ReportRun> runs, IReadOnlyList<ReportTrade> trades, int minTrades = 5) =>
        Population(runs, trades, new ReportOptions { Stats = new CellStatsOptions(MinTrades: minTrades) });

    [Fact]
    public void A_trade_is_counted_once_in_every_grouping_and_once_per_basket_under_the_basket_dimension()
    {
        for (var seed = 1; seed <= 25; seed++)
        {
            var (runs, trades) = Generate(seed);
            var population = Build(runs, trades);

            foreach (var dimensions in Subsets)
            {
                var grouping = Group(population, dimensions);
                var expected = dimensions.Contains(ReportDimension.Basket)
                    ? trades.Sum(t => Math.Max(1, t.BasketIds.Distinct().Count()))
                    : trades.Count;
                grouping.Groups.Sum(g => g.N).ShouldBe(expected, $"seed {seed}, {string.Join('+', dimensions)}");
                grouping.Groups.Select(g => g.Key.Text).Distinct().Count().ShouldBe(grouping.Groups.Count);
            }
        }
    }

    [Fact]
    public void Duplicating_every_record_of_the_input_changes_nothing_but_the_dropped_count()
    {
        var (runs, trades) = Generate(3);
        var once = Build(runs, trades);
        var twice = Build([.. runs, .. runs], [.. trades, .. trades]);

        twice.TradeCount.ShouldBe(once.TradeCount);
        twice.Evidence.DuplicateTradesDropped.ShouldBe(trades.Count);
        foreach (var dimensions in Subsets)
        {
            twice.GroupBy(dimensions).Value.Groups.ShouldBe(once.GroupBy(dimensions).Value.Groups);
        }
    }

    [Fact]
    public void A_figure_that_is_not_a_value_or_provisional_has_no_number_and_a_number_is_always_finite()
    {
        for (var seed = 1; seed <= 25; seed++)
        {
            var (runs, trades) = Generate(seed, count: 60 + seed);
            var population = Build(runs, trades, minTrades: 8);

            foreach (var dimensions in Subsets)
            {
                foreach (var group in Group(population, dimensions).Groups)
                {
                    foreach (var metric in Metrics(group))
                    {
                        var hasNumber = metric.State is MetricState.Value or MetricState.Provisional;
                        (metric.Number is not null).ShouldBe(hasNumber, $"seed {seed}, {group.Key.Text}, {metric.State}");
                        if (metric.Number is { } n)
                        {
                            double.IsFinite(n).ShouldBeTrue();
                        }
                        else
                        {
                            metric.Reason.ShouldNotBeNullOrWhiteSpace();
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void Pivot_cells_never_carry_a_number_for_an_empty_or_masked_cell()
    {
        for (var seed = 1; seed <= 10; seed++)
        {
            var (runs, trades) = Generate(seed, count: 70);
            var pivot = Build(runs, trades, minTrades: 6).Pivot(ReportDimension.Strategy, ReportDimension.Weekday, ReportMetric.ExpectancyLowerR).Value;

            pivot.Cells.Count.ShouldBe(pivot.Rows.Count * pivot.Columns.Count);
            foreach (var cell in pivot.Cells)
            {
                if (cell.N == 0)
                {
                    cell.Value.State.ShouldBe(MetricState.Empty);
                }

                if (cell.Value.State is not (MetricState.Value or MetricState.Provisional))
                {
                    cell.Value.Number.ShouldBeNull();
                }
            }
        }
    }

    [Fact]
    public void The_order_of_the_input_changes_nothing_in_groups_pivot_ranking_or_insights()
    {
        for (var seed = 1; seed <= 8; seed++)
        {
            var (runs, trades) = Generate(seed);
            var reference = Build(runs, trades);
            var random = new Random(seed * 31);
            var shuffled = Build([.. runs.OrderBy(_ => random.Next())], [.. trades.OrderBy(_ => random.Next())]);

            foreach (var dimensions in Subsets)
            {
                shuffled.GroupBy(dimensions).Value.ShouldBe(reference.GroupBy(dimensions).Value);
            }

            shuffled.Pivot(ReportDimension.Side, ReportDimension.Timeframe, ReportMetric.ExpectancyR).Value.ShouldBe(reference.Pivot(ReportDimension.Side, ReportDimension.Timeframe, ReportMetric.ExpectancyR).Value);
            shuffled.FindWhatWorks().Value.ShouldBe(reference.FindWhatWorks().Value);
            shuffled.Insights().ShouldBe(reference.Insights());
            shuffled.Evidence.ShouldBe(reference.Evidence);
        }
    }

    [Fact]
    public void Adding_a_trade_to_one_group_leaves_the_own_figures_of_every_other_group_unchanged()
    {
        var (runs, trades) = Generate(11);
        var before = Group(Build(runs, trades), ReportDimension.Strategy, ReportDimension.Side);
        ReportTrade added = Trade("run-ORB", "ORB-extra", 2.5, "ORB", "15m", "BUY", "AAA", day: 70);
        var after = Group(Build(runs, [.. trades, added]), ReportDimension.Strategy, ReportDimension.Side);

        foreach (var group in before.Groups)
        {
            var other = Only(after, group.Key.Text);
            if (group.Key.Text == "Strategy=ORB|Side=BUY")
            {
                other.N.ShouldBe(group.N + 1);
                continue;
            }

            // the unshrunk figures belong to the group alone; the shrunk ones move only through their parent pool (stated in ADR 0099)
            other.N.ShouldBe(group.N);
            other.WinRate.ShouldBe(group.WinRate);
            other.ExpectancyR.ShouldBe(group.ExpectancyR);
            other.TotalR.ShouldBe(group.TotalR);
            other.ProfitFactor.ShouldBe(group.ProfitFactor);
            other.MaxDrawdownR.ShouldBe(group.MaxDrawdownR);
            other.LongestLossStreak.ShouldBe(group.LongestLossStreak);
            other.Evidence.ShouldBe(group.Evidence);
        }
    }

    [Fact]
    public void Adding_a_trade_never_removes_one_and_the_totals_grow_by_exactly_one()
    {
        var (runs, trades) = Generate(5);
        var before = Build(runs, trades);
        var after = Build(runs, [.. trades, Trade("run-GAP", "GAP-extra", -1, "GAP")]);

        after.TradeCount.ShouldBe(before.TradeCount + 1);
        Group(after, ReportDimension.Side).Groups.Sum(g => g.N).ShouldBe(Group(before, ReportDimension.Side).Groups.Sum(g => g.N) + 1);
    }
}
