using XDrishti.Domain.Reporting;
using static XDrishti.Domain.Tests.Reporting.ReportFixtures;

namespace XDrishti.Domain.Tests.Reporting;

/// <summary>P8-06: the equity and drawdown series, the R distribution and the trade slices of the engine, against hand tables. A group without enough trades or with an unrecorded label has no points and no bins, only the reason.</summary>
public sealed class ReportSeriesTests
{
    private static ReportPopulation Five() => Population(
        [Run("r1")],
        [Trade("r1", "k0", 1, day: 0), Trade("r1", "k1", -2, day: 1), Trade("r1", "k2a", 1.5, day: 2), Trade("r1", "k2b", 0.5, day: 2), Trade("r1", "k3", -1, day: 3)]);

    [Fact]
    public void The_equity_curve_is_the_running_sum_by_exit_date_with_the_fall_from_the_running_peak()
    {
        var equity = Five().Equity([]);

        equity.IsSuccess.ShouldBeTrue();
        var series = equity.Value.Series.Single();
        series.Key.Text.ShouldBe("all");
        series.N.ShouldBe(5);
        series.TotalR.State.ShouldBe(MetricState.Value);
        series.TotalR.Number!.Value.ShouldBe(0.0, 1e-12);
        series.Points.Select(p => (p.Date, p.CumulativeR, p.DrawdownR, p.Trades)).ShouldBe(
        [
            (Day0, 1.0, 0.0, 1),
            (Day0.AddDays(1), -1.0, 2.0, 1),
            (Day0.AddDays(2), 1.0, 0.0, 2),
            (Day0.AddDays(3), 0.0, 1.0, 1),
        ]);
        series.Points.Sum(p => p.Trades).ShouldBe(series.N);
        equity.Value.NonAdditive.ShouldBeFalse();
    }

    [Fact]
    public void The_last_point_of_the_curve_is_the_total_and_the_deepest_drawdown_is_the_maximum_drawdown_of_the_group()
    {
        var population = Five();

        var series = population.Equity([]).Value.Series.Single();
        var all = Group(population).Groups.Single();

        series.Points[^1].CumulativeR.ShouldBe(all.TotalR.Number!.Value, 1e-12);
        series.Points.Max(p => p.DrawdownR).ShouldBe(all.MaxDrawdownR.Number!.Value, 1e-12);
    }

    [Fact]
    public void A_group_below_the_minimum_sample_has_no_curve_only_the_reason()
    {
        var population = Population([Run("r1")], [Trade("r1", "a", 1, side: "BUY", day: 0), Trade("r1", "b", -1, side: "BUY", day: 1), Trade("r1", "c", 2, side: "BUY", day: 2), Trade("r1", "d", 1, side: "SELL", day: 3)]);

        var equity = population.Equity([ReportDimension.Side]).Value;

        equity.Series.Select(s => s.Key.Text).ShouldBe(["Side=BUY", "Side=SELL"]);
        equity.Series[0].TotalR.State.ShouldBe(MetricState.Value);
        equity.Series[0].Points.Count.ShouldBe(3);
        equity.Series[1].TotalR.State.ShouldBe(MetricState.Insufficient);
        equity.Series[1].TotalR.Number.ShouldBeNull();
        equity.Series[1].Points.ShouldBeEmpty();
        equity.Series[1].N.ShouldBe(1);
    }

    [Fact]
    public void A_label_no_run_recorded_makes_the_series_unavailable_and_names_the_run()
    {
        var population = Population([Run("r1", exit: null)], Series("r1", "ORB", [1, -1, 1, 1]));

        var series = population.Equity([ReportDimension.Exit]).Value.Series.Single();

        series.TotalR.State.ShouldBe(MetricState.Unavailable);
        series.TotalR.Reason!.ShouldContain("r1");
        series.Points.ShouldBeEmpty();
    }

    [Fact]
    public void A_provisional_run_marks_the_total_provisional_and_keeps_the_points()
    {
        var population = Population([Run("r1", provisional: true)], Series("r1", "ORB", [1, -1, 1]));

        var series = population.Equity([]).Value.Series.Single();

        series.TotalR.State.ShouldBe(MetricState.Provisional);
        series.TotalR.Number.ShouldNotBeNull();
        series.Points.Count.ShouldBe(3);
        series.Evidence.Provisional.ShouldBeTrue();
    }

    [Fact]
    public void A_trade_in_two_baskets_is_on_two_curves_and_the_report_says_it_is_not_additive()
    {
        var population = Population([Run("r1")], Series("r1", "ORB", [1, 1, -1], baskets: ["B1", "B2"]));

        var equity = population.Equity([ReportDimension.Basket]).Value;

        equity.NonAdditive.ShouldBeTrue();
        equity.Series.Select(s => s.Key.Text).ShouldBe(["Basket=B1", "Basket=B2"]);
        equity.Series.ShouldAllBe(s => s.N == 3 && s.Points.Count == 3);
    }

    [Fact]
    public void A_dimension_with_no_data_and_a_repeated_dimension_are_refused()
    {
        var population = Five();

        population.Equity([ReportDimension.Regime]).Error.Code.ShouldBe("reports.dimension_unavailable");
        population.Equity([ReportDimension.Side, ReportDimension.Side]).Error.Code.ShouldBe("reports.invalid");
    }

    [Fact]
    public void An_empty_population_has_no_series()
    {
        var population = Population([Run("r1")], []);

        population.Equity([]).Value.Series.ShouldBeEmpty();
        population.Distribution([], 0.5).Value.Groups.ShouldBeEmpty();
        population.Trades(0, 10).ShouldBeEmpty();
    }

    [Fact]
    public void The_distribution_counts_each_trade_in_exactly_one_bin_with_open_bins_at_both_ends()
    {
        var population = Population([Run("r1")], Series("r1", "ORB", [-5, -4, -0.2, 0, 0.49, 0.5, 5.99, 6, 9]));

        var distribution = population.Distribution([], 0.5);

        distribution.IsSuccess.ShouldBeTrue();
        distribution.Value.BinWidthR.ShouldBe(0.5);
        var group = distribution.Value.Groups.Single();
        group.N.ShouldBe(9);
        group.MeanR.State.ShouldBe(MetricState.Value);
        group.MeanR.Number!.Value.ShouldBe(12.78 / 9, 1e-9);
        group.Bins.Count.ShouldBe(22);
        group.Bins.Sum(b => b.Count).ShouldBe(9);
        group.Bins[0].ShouldBe(new DistributionBin(null, -4, 1));
        group.Bins[1].ShouldBe(new DistributionBin(-4, -3.5, 1));
        group.Bins.Single(b => b.LowerR == -0.5).Count.ShouldBe(1);
        group.Bins.Single(b => b.LowerR == 0).Count.ShouldBe(2);
        group.Bins.Single(b => b.LowerR == 0.5).Count.ShouldBe(1);
        group.Bins.Single(b => b.LowerR == 5.5).Count.ShouldBe(1);
        group.Bins[^1].ShouldBe(new DistributionBin(6, null, 2));
    }

    [Fact]
    public void A_group_below_the_minimum_sample_has_no_bins_and_an_unrecorded_label_is_unavailable()
    {
        var population = Population([Run("r1", exit: null)], Series("r1", "ORB", [1, -1, 1, 1]).Concat(Series("r1", "XYZ", [1])).ToList());

        var byStrategy = population.Distribution([ReportDimension.Strategy], 0.5).Value;
        var byExit = population.Distribution([ReportDimension.Exit], 0.5).Value;

        byStrategy.Groups.Select(g => g.Key.Text).ShouldBe(["Strategy=ORB", "Strategy=XYZ"]);
        byStrategy.Groups[0].Bins.ShouldNotBeEmpty();
        byStrategy.Groups[1].MeanR.State.ShouldBe(MetricState.Insufficient);
        byStrategy.Groups[1].Bins.ShouldBeEmpty();
        byExit.Groups.Single().MeanR.State.ShouldBe(MetricState.Unavailable);
        byExit.Groups.Single().Bins.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(6)]
    [InlineData(double.NaN)]
    public void A_bin_width_outside_a_tenth_to_five_R_is_refused(double width)
    {
        var distribution = Five().Distribution([], width);

        distribution.IsFailure.ShouldBeTrue();
        distribution.Error.Code.ShouldBe("reports.invalid");
    }

    [Fact]
    public void A_slice_of_the_trades_is_in_exit_order_and_a_slice_past_the_end_is_empty()
    {
        var population = Five();

        var all = population.Trades(0, 100);
        var middle = population.Trades(1, 2);

        all.Select(t => t.TradeKey).ShouldBe(["k0", "k1", "k2a", "k2b", "k3"]);
        all.Select(t => t.R).ShouldBe([1.0, -2.0, 1.5, 0.5, -1.0]);
        middle.Select(t => t.TradeKey).ShouldBe(["k1", "k2a"]);
        population.Trades(5, 10).ShouldBeEmpty();
        population.Trades(0, 0).ShouldBeEmpty();
        Should.Throw<ArgumentOutOfRangeException>(() => population.Trades(-1, 1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Over_random_populations_every_trade_is_on_one_curve_in_one_bin_and_in_the_list_and_the_curve_ends_at_the_total(int seed)
    {
        var random = new Random(seed);
        var trades = new List<ReportTrade>();
        for (var i = 0; i < 300; i++)
        {
            var strategy = random.Next(3) switch { 0 => "A", 1 => "B", _ => "C" };
            trades.Add(Trade("r1", $"{strategy}-{i:D4}", Math.Round((random.NextDouble() * 14) - 6, 2), strategy, symbol: "S" + random.Next(5), day: random.Next(120)));
        }

        var population = Population([Run("r1")], trades, Small(minTrades: 30));

        var equity = population.Equity([ReportDimension.Strategy]).Value;
        var distribution = population.Distribution([ReportDimension.Strategy], 0.5).Value;
        var groups = Group(population, ReportDimension.Strategy);

        equity.Series.Sum(s => s.N).ShouldBe(300);
        foreach (var series in equity.Series)
        {
            series.Points.Sum(p => p.Trades).ShouldBe(series.N);
            series.Points.Select(p => p.Date).ShouldBe(series.Points.Select(p => p.Date).Order().Distinct());
            series.Points[^1].CumulativeR.ShouldBe(Only(groups, series.Key.Text).TotalR.Number!.Value, 1e-9);
        }

        distribution.Groups.ShouldAllBe(g => g.Bins.Sum(b => b.Count) == g.N);
        population.Trades(0, 1000).Count.ShouldBe(population.TradeCount);
        population.Trades(0, 1000).Select(t => t.ExitDate).ShouldBe(population.Trades(0, 1000).Select(t => t.ExitDate).Order());
    }

    [Fact]
    public void A_trade_line_carries_the_labels_of_its_run_and_the_net_R_of_a_net_report()
    {
        var population = Population(
            [Run("r1", costed: true, style: "swing", exit: "FIXED_RR{rr=2}")],
            [Trade("r1", "k0", 1.0, net: 0.8, baskets: ["B1"])],
            new ReportOptions { Basis = ReportBasis.Net, NetBasisConfirmation = Confirmed, Stats = Small().Stats, MinWins = 1, MinNonWins = 1 });

        var line = population.Trades(0, 1).Single();

        line.R.ShouldBe(0.8);
        line.Style.ShouldBe("swing");
        line.Exit.ShouldBe("FIXED_RR{rr=2}");
        line.BasketIds.ShouldBe(["B1"]);
        population.Evidence.Population.Basis.ShouldBe(ReportBasis.Net);
    }
}
