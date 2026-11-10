using XDrishti.Domain.Baskets.Derived;

namespace XDrishti.Domain.Tests.Baskets.Derived;

/// <summary>The hand-worked numbers of the spec (BK-02 acceptance criteria 1, 2, 3, 5, 7, 8, 13).</summary>
public sealed class IndicatorKitTests
{
    [Fact]
    public void Wilder_atr_step_gives_2_1_and_atr_percent_2_1_when_the_close_is_100()
    {
        var atr = IndicatorKit.WilderStep(2.0, 3.4, 14);

        atr.ShouldBe(2.1, 1e-12);
        (atr / 100).ShouldBe(0.021, 1e-12);
    }

    [Fact]
    public void True_range_is_the_largest_of_the_range_and_the_two_gaps()
    {
        IndicatorKit.TrueRange(104, 100, 98).ShouldBe(6);
    }

    [Fact]
    public void Atr_of_a_constant_range_equals_that_range()
    {
        IndicatorKit.Atr(DerivedFixtures.Flat(30, 100, 2), 14).ShouldBe(4, 1e-12);
        IndicatorKit.Atr(DerivedFixtures.Flat(14, 100, 2), 14).ShouldBeNaN();
    }

    [Fact]
    public void Realised_volatility_of_a_one_percent_daily_stdev_is_15_8745_percent()
    {
        double[] returns = [0.01, 0, -0.01];

        IndicatorKit.StdDev(returns).ShouldBe(0.01, 1e-12);
        IndicatorKit.RealisedVolatility(returns).ShouldBe(0.158745, 1e-6);
        IndicatorKit.AnnualisationFactor.ShouldBe(15.8745, 1e-4);
    }

    [Fact]
    public void Median_traded_value_ignores_one_block_day_and_averages_the_middle_two_for_20_values()
    {
        var bars = DerivedFixtures.Flat(20, 100, 0, 1_000_000); // Rs 10 crore a day
        bars[7] = bars[7] with { Volume = 20_000_000 }; // Rs 200 crore block

        IndicatorKit.MedianTradedValue(bars, 20).ShouldBe(100_000_000);
        IndicatorKit.Mean([.. bars.Select(b => b.Close * b.Volume)]).ShouldBe(195_000_000);
        IndicatorKit.Median([1, 9, 3, 7]).ShouldBe(5);
    }

    [Fact]
    public void Beta_of_a_stock_moving_one_and_a_half_times_the_market_is_1_5()
    {
        double[] nifty = [0.01, -0.01, 0.02, -0.02];
        double[] stock = [0.015, -0.015, 0.03, -0.03];

        IndicatorKit.Beta(stock, nifty).ShouldBe(1.5, 1e-12);
        IndicatorKit.Beta(stock, new double[4]).ShouldBeNaN();
    }

    [Fact]
    public void Variance_ratio_is_0_8_for_variances_0_0004_and_0_0001()
    {
        IndicatorKit.VarianceRatio(0.0004, 0.0001, 5).ShouldBe(0.8, 1e-12);
    }

    [Fact]
    public void Efficiency_ratio_is_10_over_22_for_the_worked_path_and_1_for_a_straight_climb()
    {
        IndicatorKit.EfficiencyRatio(DerivedFixtures.FromCloses([100, 102, 99, 103, 100, 110]), 5).ShouldBe(10.0 / 22, 1e-12);
        IndicatorKit.EfficiencyRatio(DerivedFixtures.FromCloses([100, 102, 104, 106, 108, 110]), 5).ShouldBe(1, 1e-12);
    }

    [Fact]
    public void Dx_is_50_for_di_30_and_10_and_14_29_for_20_and_15_and_a_wilder_mean_of_equal_values_is_that_value()
    {
        IndicatorKit.Dx(30, 10).ShouldBe(50, 1e-12);
        IndicatorKit.Dx(20, 15).ShouldBe(14.2857, 1e-4);
        IndicatorKit.WilderMean(Enumerable.Repeat(50.0, 14).ToArray(), 14).ShouldBe(50, 1e-12);
    }

    [Fact]
    public void Adx_of_a_steady_climb_is_100_and_of_too_few_bars_is_nan()
    {
        var climb = DerivedFixtures.FromCloses(Enumerable.Range(0, 40).Select(i => 100.0 + i).ToList(), 0.5);

        IndicatorKit.Adx(climb, 14).ShouldBe(100, 1e-9);
        IndicatorKit.Adx(climb.AsSpan(0, 20), 14).ShouldBeNaN();
    }

    [Theory]
    [InlineData(1.0, 0.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.0, 0.70710678)]
    [InlineData(-1.0, 1.0)]
    public void Correlation_distance_table(double rho, double expected)
    {
        IndicatorKit.CorrelationDistance(rho).ShouldBe(expected, 1e-7);
    }

    [Fact]
    public void Average_linkage_joins_the_correlated_pair_first_and_labels_are_stable()
    {
        var ab = IndicatorKit.CorrelationDistance(0.9);
        var c = IndicatorKit.CorrelationDistance(0.1);
        double[] d = [0, ab, c, ab, 0, c, c, c, 0];

        ab.ShouldBe(0.2236, 1e-4);
        c.ShouldBe(0.6708, 1e-4);
        IndicatorKit.AverageLinkage(d, 3, 2).ShouldBe([1, 1, 2]);
        IndicatorKit.AverageLinkage(d, 3, 1).ShouldBe([1, 1, 1]);
        IndicatorKit.AverageLinkage(d, 3, 3).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public void Adjusted_rand_index_is_1_for_the_same_grouping_under_other_names()
    {
        IndicatorKit.AdjustedRandIndex([1, 1, 2, 2, 3], [7, 7, 5, 5, 9]).ShouldBe(1, 1e-12);
        IndicatorKit.AdjustedRandIndex([1, 1, 2, 2], [1, 2, 1, 2]).ShouldBeLessThan(0.1);
    }

    [Fact]
    public void Ranks_share_the_average_rank_for_ties_and_spearman_is_1_for_a_monotone_pair()
    {
        IndicatorKit.Ranks([10, 20, 20, 30]).ShouldBe([1, 2.5, 2.5, 4]);
        IndicatorKit.Spearman([1, 2, 3, 4], [1, 8, 27, 64]).ShouldBe(1, 1e-12);
    }

    [Fact]
    public void Winsorise_pulls_an_outlier_in_to_the_upper_quantile()
    {
        var w = IndicatorKit.Winsorise([.. Enumerable.Range(1, 99).Select(i => (double)i), 1000], 0.01);

        w.Max().ShouldBe(108.01, 1e-9);
        w.Min().ShouldBe(1.99, 1e-9);
        w.Length.ShouldBe(100);
    }

    [Fact]
    public void A_375_bar_session_allows_18_zero_volume_minutes()
    {
        DerivedScorers.MaxZeroVolumeBars(375, 0.05).ShouldBe(18);
    }
}
