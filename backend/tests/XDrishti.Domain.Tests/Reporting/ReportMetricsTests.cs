using XDrishti.Domain.Learning;
using XDrishti.Domain.Reporting;
using static XDrishti.Domain.Tests.Reporting.ReportFixtures;

namespace XDrishti.Domain.Tests.Reporting;

/// <summary>The figures of a group against hand tables, the shrinkage against the P6-01 calculator, the minimum sample and the six states (P8-05, ADR 0099).</summary>
public sealed class ReportMetricsTests
{
    [Fact]
    public void Hand_table_of_six_trades_gives_every_figure()
    {
        // R in exit order: +2 -1 -1 +1 -1 0 ; sessions held 1 2 3 1 1 2. Wins 2, losses 3, one flat trade.
        double[] rs = [2, -1, -1, 1, -1, 0];
        int[] sessions = [1, 2, 3, 1, 1, 2];
        var trades = rs.Select((r, i) => Trade("r1", $"T{i}", r, day: i, sessions: sessions[i])).ToList();
        var population = Population([Run("r1")], trades);

        var group = Only(Group(population), "all");

        group.N.ShouldBe(6);
        group.Wins.ShouldBe(2);
        group.WinRate.Number!.Value.ShouldBe(1.0 / 3, 1e-12);
        group.AvgWinR.Number!.Value.ShouldBe(1.5, 1e-12);
        group.AvgLossR.Number!.Value.ShouldBe(-1, 1e-12);
        group.ExpectancyR.Number!.Value.ShouldBe(0, 1e-12);
        group.ProfitFactor.Number!.Value.ShouldBe(1, 1e-12);
        group.TotalR.Number!.Value.ShouldBe(0, 1e-12);
        group.MaxDrawdownR.Number!.Value.ShouldBe(2, 1e-12);
        group.LongestLossStreak.Number!.Value.ShouldBe(2, 1e-12);
        group.AvgHoldingSessions.Number!.Value.ShouldBe(10.0 / 6, 1e-12);
        group.ShrunkWinRate.Number!.Value.ShouldBe(1.0 / 3, 1e-12);
        group.WinRateLower.Number!.Value.ShouldBe(BetaDistribution.Quantile(0.05, 2, 4), 1e-12);
        group.ShrunkExpectancyR.Number!.Value.ShouldBe(0, 1e-12);
        var lower = BetaDistribution.Quantile(0.05, 2, 4);
        group.ExpectancyLowerR.Number!.Value.ShouldBe((lower * 1.5) + ((1 - lower) * -0.75), 1e-12);
        group.Evidence.N.ShouldBe(6);
        group.Evidence.FirstDate.ShouldBe(Day0);
        group.Evidence.LastDate.ShouldBe(Day0.AddDays(5));
        group.Evidence.Basis.ShouldBe(ReportBasis.Gross);
        group.Evidence.Provisional.ShouldBeFalse();
        group.Evidence.RunKeys.ShouldBe(["r1"]);
    }

    [Fact]
    public void Drawdown_and_streak_follow_the_exit_order_not_the_input_order()
    {
        // exits on days 0..4: +1 +1 -1 -1 -1 ; handed over reversed. Running R 1 2 1 0 -1, peak 2, drawdown 3, streak 3.
        double[] rs = [1, 1, -1, -1, -1];
        var trades = rs.Select((r, i) => Trade("r1", $"T{i}", r, day: i)).Reverse().ToList();

        var group = Only(Group(Population([Run("r1")], trades)), "all");

        group.MaxDrawdownR.Number!.Value.ShouldBe(3, 1e-12);
        group.LongestLossStreak.Number!.Value.ShouldBe(3, 1e-12);
    }

    [Fact]
    public void A_group_with_no_losing_trade_has_no_profit_factor_and_no_expectancy_bound_but_a_plain_expectancy()
    {
        var population = Population([Run("r1")], Series("r1", "ORB", Repeat(1, 4)));

        var group = Only(Group(population), "all");

        group.ExpectancyR.State.ShouldBe(MetricState.Value);
        group.ProfitFactor.State.ShouldBe(MetricState.NotEvaluable);
        group.ProfitFactor.Reason.ShouldBe("no losing trade");
        group.AvgLossR.State.ShouldBe(MetricState.NotEvaluable);
        group.ExpectancyLowerR.State.ShouldBe(MetricState.NotEvaluable);
        group.ExpectancyLowerR.Number.ShouldBeNull();
    }

    [Fact]
    public void With_no_prior_strength_the_shrunk_expectancy_is_the_plain_expectancy()
    {
        double[] rs = [2, -1, -1, 1, -1, 0, 0.5, -0.5];
        var trades = rs.Select((r, i) => Trade("r1", $"T{i}", r, day: i)).ToList();

        var group = Only(Group(Population([Run("r1")], trades, Small(3, strength: 0))), "all");

        group.ShrunkExpectancyR.Number!.Value.ShouldBe(group.ExpectancyR.Number!.Value, 1e-12);
    }

    [Fact]
    public void The_shrinkage_equals_the_cell_statistics_calculator_on_the_same_trades()
    {
        var asOf = Day0.AddDays(30);
        double[] orb = [1, -1, 2, -1, -0.5, 3];
        double[] vwap = [-1, -1, 0.5, -2, 1];
        List<ReportTrade> trades = [];
        trades.AddRange(orb.Select((r, i) => new ReportTrade("r1", $"ORB{i:D2}", "ORB", "15m", "BUY", InstrumentOf("AAA"), "AAA", new([]), asOf, asOf, r, null, 1)));
        trades.AddRange(vwap.Select((r, i) => new ReportTrade("r2", $"VWAP{i:D2}", "VWAP", "15m", "BUY", InstrumentOf("AAA"), "AAA", new([]), asOf, asOf, r, null, 1)));
        var stats = new CellStatsOptions(PriorStrength: 20, MinTrades: 3, CredibleLevel: 0.9);
        var population = Population([Run("r1"), Run("r2")], trades, new ReportOptions { Stats = stats });
        CellId Cell(string strategy) => new(strategy, "15m", "BUY", "intraday", "FIXED_RR");
        var rows = CellStatsCalculator.Compute(
            [
                new CellInput(Cell("ORB"), false, [.. orb.Select(r => new CellTrade(asOf, r, 0, 0))]),
                new CellInput(Cell("VWAP"), false, [.. vwap.Select(r => new CellTrade(asOf, r, 0, 0))]),
            ],
            asOf,
            stats).Where(r => r.Window == CellWindow.All).ToDictionary(r => r.Cell.Strategy);

        var grouping = Group(population, ReportDimension.Strategy, ReportDimension.Timeframe, ReportDimension.Side, ReportDimension.Style, ReportDimension.Exit);

        grouping.Groups.Count.ShouldBe(2);
        foreach (var group in grouping.Groups)
        {
            var row = rows[group.Key.ValueOf(ReportDimension.Strategy)!];
            group.N.ShouldBe(row.N);
            group.PriorRate!.Value.ShouldBe(row.PriorRate, 1e-12);
            group.ShrunkWinRate.Number!.Value.ShouldBe(row.WinRatePosterior, 1e-12);
            group.WinRateLower.Number!.Value.ShouldBe(row.WinRateLower, 1e-12);
            group.WinRate.Number!.Value.ShouldBe(row.WinRateRaw!.Value, 1e-12);
            group.ExpectancyR.Number!.Value.ShouldBe(row.ExpectancyR!.Value, 1e-12);
            group.ProfitFactor.Number!.Value.ShouldBe(row.ProfitFactor!.Value, 1e-12);
            group.MaxDrawdownR.Number!.Value.ShouldBe(row.MaxDrawdownR, 1e-12);
            group.AvgWinR.Number!.Value.ShouldBe(row.AvgWinR!.Value, 1e-12);
            group.AvgLossR.Number!.Value.ShouldBe(row.AvgLossR!.Value, 1e-12);
        }
    }

    [Fact]
    public void Below_the_minimum_sample_no_estimate_is_given_only_the_count()
    {
        var population = Population([Run("r1")], Series("r1", "ORB", WinsAndLosses(15, 14)), new ReportOptions());

        var group = Only(Group(population), "all");

        group.N.ShouldBe(29);
        foreach (var metric in Metrics(group).Where(m => m.State != MetricState.Value))
        {
            metric.State.ShouldBe(MetricState.Insufficient);
            metric.Number.ShouldBeNull();
            metric.Reason.ShouldBe("29 trades, at least 30 needed");
        }

        group.Get(ReportMetric.Trades).Number.ShouldBe(29);
        group.ExpectancyR.State.ShouldBe(MetricState.Insufficient);
        group.PriorRate.ShouldBeNull();
    }

    [Fact]
    public void At_the_minimum_sample_the_figures_are_values()
    {
        var population = Population([Run("r1")], Series("r1", "ORB", WinsAndLosses(15, 15)), new ReportOptions());

        var group = Only(Group(population), "all");

        group.ExpectancyR.State.ShouldBe(MetricState.Value);
        group.ExpectancyLowerR.State.ShouldBe(MetricState.Value);
        group.ExpectancyLowerR.Number!.Value.ShouldBeLessThan(group.ShrunkExpectancyR.Number!.Value);
    }

    [Fact]
    public void A_run_marked_provisional_makes_every_number_provisional_and_the_evidence_says_so()
    {
        var population = Population([Run("r1", provisional: true)], Series("r1", "ORB", WinsAndLosses(3, 2)));

        var group = Only(Group(population), "all");

        group.Evidence.Provisional.ShouldBeTrue();
        group.ExpectancyR.State.ShouldBe(MetricState.Provisional);
        group.ExpectancyR.Number.ShouldNotBeNull();
        group.ExpectancyR.Reason.ShouldBe("rests on a provisional run: r1");
        population.Evidence.Population.Provisional.ShouldBeTrue();
    }

    [Fact]
    public void A_group_by_a_label_no_run_recorded_is_unavailable_with_the_run_named_and_has_no_number()
    {
        var population = Population([Run("r1", exit: null)], Series("r1", "ORB", WinsAndLosses(3, 2)));

        var grouping = Group(population, ReportDimension.Exit);

        var group = grouping.Groups.Single();
        group.Key.Text.ShouldBe("Exit=(not recorded)");
        group.N.ShouldBe(5);
        foreach (var metric in Metrics(group).Where(m => m.State != MetricState.Value))
        {
            metric.State.ShouldBe(MetricState.Unavailable);
            metric.Reason.ShouldBe("exit not recorded for run r1");
            metric.Number.ShouldBeNull();
        }

        group.ExpectancyR.State.ShouldBe(MetricState.Unavailable);
        population.Evidence.UnavailableInputs.ShouldContain("exit not recorded for run r1");
    }

    [Fact]
    public void A_figure_that_is_not_finite_is_not_evaluable_never_a_number()
    {
        Metric.Of(double.NaN).State.ShouldBe(MetricState.NotEvaluable);
        Metric.Of(double.PositiveInfinity).Number.ShouldBeNull();
        Metric.OfProvisional(double.NaN, "x").State.ShouldBe(MetricState.NotEvaluable);
        Metric.Of(0).State.ShouldBe(MetricState.Value);
        Metric.Of(0).Number.ShouldBe(0);
    }

    [Fact]
    public void Only_a_plain_value_can_be_made_provisional()
    {
        Metric.Of(1).AsProvisional("why").State.ShouldBe(MetricState.Provisional);
        Metric.Insufficient("few").AsProvisional("why").State.ShouldBe(MetricState.Insufficient);
        Metric.Empty("none").AsProvisional("why").Number.ShouldBeNull();
    }
}
