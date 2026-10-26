using XDrishti.Domain.Paper;

namespace XDrishti.Domain.Tests.Paper;

/// <summary>Spec P10-04 criteria 6 to 9: the state thresholds, the E3 and E4 verdict tables and the early warning.</summary>
public sealed class PaperExitStatisticsTests
{
    private static BandResult Band(double mean = 0.2, double low = 0.05, double high = 0.4, double p05 = -0.3, double p95 = 0.3, string position = "within", double fall = 2, double p90 = 3, string fallPosition = "within") =>
        new(60, 10_000, 1, mean, low, high, p05, 0, p95, 50, position, fall, 2, p90, 4, fallPosition, []);

    [Theory]
    [InlineData(0, "insufficient")]
    [InlineData(29, "insufficient")]
    [InlineData(30, "provisional")]
    [InlineData(59, "provisional")]
    [InlineData(60, "value")]
    public void The_state_follows_the_count(int n, string state) => PaperExitStatistics.StateOf(n).ShouldBe(state);

    [Fact]
    public void A_positive_mean_inside_the_band_with_an_interval_above_zero_is_met_with_no_caveat()
    {
        var c = PaperExitStatistics.Evaluate(60, true, true, Band(), null);

        c.Expectancy.Verdict.ShouldBe("met");
        c.Expectancy.Label.ShouldNotContain("point estimate");
        c.Drawdown.Verdict.ShouldBe("met");
        c.Drawdown.Label.ShouldContain("2.000R");
        c.Drawdown.Label.ShouldContain("3.000R");
    }

    [Fact]
    public void An_interval_that_includes_zero_and_a_mean_above_the_band_carry_their_caveats()
    {
        var c = PaperExitStatistics.Evaluate(60, true, true, Band(low: -0.1, position: "above"), null);

        c.Expectancy.Verdict.ShouldBe("met");
        c.Expectancy.Label.ShouldContain("point estimate; 90% interval [-0.100R, +0.400R] includes 0: positive expectancy is not statistically established");
        c.Expectancy.Label.ShouldContain("(above the band: luck or a leak is possible)");
    }

    [Fact]
    public void A_mean_at_or_below_zero_or_below_the_band_is_not_met_with_the_numbers()
    {
        PaperExitStatistics.Evaluate(60, true, true, Band(mean: 0), null).Expectancy.Verdict.ShouldBe("not_met");
        var below = PaperExitStatistics.Evaluate(60, true, true, Band(mean: -0.5, position: "below"), null).Expectancy;
        below.Verdict.ShouldBe("not_met");
        below.Label.ShouldContain("-0.500R");
    }

    [Fact]
    public void A_fall_beyond_the_ninetieth_percentile_is_not_met()
    {
        var c = PaperExitStatistics.Evaluate(60, true, true, Band(fall: 5, fallPosition: "above"), null);

        c.Drawdown.Verdict.ShouldBe("not_met");
        c.Drawdown.Label.ShouldContain("5.000R");
    }

    [Fact]
    public void Unconfirmed_costs_gross_r_few_trades_or_no_band_make_both_statistics_not_evaluable_with_every_reason()
    {
        var unconfirmed = PaperExitStatistics.Evaluate(60, true, false, Band(), null);
        unconfirmed.Expectancy.Verdict.ShouldBe("not_evaluable");
        unconfirmed.Expectancy.Label.ShouldContain("costs not confirmed");
        unconfirmed.Drawdown.Verdict.ShouldBe("not_evaluable");

        var gross = PaperExitStatistics.Evaluate(60, false, true, Band(), null);
        gross.Expectancy.Label.ShouldContain("gross");

        var few = PaperExitStatistics.Evaluate(59, true, true, Band(), null);
        few.Expectancy.Verdict.ShouldBe("not_evaluable");
        few.Expectancy.Label.ShouldContain("59 counted trades, 60 are needed");

        var none = PaperExitStatistics.Evaluate(60, true, true, null, "basis differs in X");
        none.Expectancy.Label.ShouldContain("basis differs in X");
    }

    [Fact]
    public void The_early_warning_needs_thirty_trades_and_an_interval_wholly_below_the_band()
    {
        var worse = Band(low: -0.9, high: -0.4);

        PaperExitStatistics.Evaluate(30, false, false, worse, null).EarlyWarning.ShouldBe(PaperExitStatistics.EarlyWarningText);
        PaperExitStatistics.Evaluate(29, false, false, worse, null).EarlyWarning.ShouldBeNull();
        PaperExitStatistics.Evaluate(60, true, true, Band(), null).EarlyWarning.ShouldBeNull();
        PaperExitStatistics.Evaluate(60, true, true, null, "no band").EarlyWarning.ShouldBeNull();
    }
}
