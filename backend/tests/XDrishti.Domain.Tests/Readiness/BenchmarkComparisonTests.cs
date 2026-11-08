using XDrishti.Domain.Readiness;

namespace XDrishti.Domain.Tests.Readiness;

/// <summary>CF-11 row 13 (ADR 0227): the daily Sharpe of the strategy against the buy-and-hold over the same days, worked by hand. Strictly greater passes; an equal Sharpe does not; what cannot be measured is no answer.</summary>
public sealed class BenchmarkComparisonTests
{
    [Theory]
    [InlineData(new[] { 0.01, 0.03 }, 1.4142135623730951)] // mean 0.02, sample variance 0.0002, sd 0.0141421
    [InlineData(new[] { 0.01, 0.02, 0.03 }, 2.0)] // mean 0.02, sample variance 0.0001
    [InlineData(new[] { -0.01, 0.01, -0.01, 0.01 }, 0.0)] // mean 0
    [InlineData(new[] { 1.0, 2.0, 3.0, 4.0 }, 1.9364916731037085)] // mean 2.5, sample variance 5/3
    public void The_daily_sharpe_is_the_mean_over_the_sample_standard_deviation(double[] returns, double expected) =>
        BenchmarkComparison.DailySharpe(returns).ShouldNotBeNull().ShouldBe(expected, 1e-9);

    [Theory]
    [InlineData(new double[] { })]
    [InlineData(new[] { 0.01 })]
    [InlineData(new[] { 0.01, 0.01, 0.01 })] // no variance
    [InlineData(new[] { 0.0, 0.0, 0.0, 0.0 })]
    [InlineData(new[] { 0.01, double.NaN, 0.02 })]
    [InlineData(new[] { 0.01, double.PositiveInfinity, 0.02 })]
    public void A_series_that_cannot_be_measured_has_no_sharpe(double[] returns) => BenchmarkComparison.DailySharpe(returns).ShouldBeNull();

    [Fact]
    public void The_strategy_beats_the_hold_only_when_its_sharpe_is_strictly_greater()
    {
        BenchmarkComparison.Beats(2.0, 1.9999999).ShouldBe(true);
        BenchmarkComparison.Beats(2.0, 2.0).ShouldBe(false, "equal is not beating");
        BenchmarkComparison.Beats(1.9999999, 2.0).ShouldBe(false);
        BenchmarkComparison.Beats(-0.5, -1.0).ShouldBe(true, "negative Sharpes compare the same way");
    }

    [Theory]
    [InlineData(null, 1.0)]
    [InlineData(1.0, null)]
    [InlineData(null, null)]
    [InlineData(double.NaN, 1.0)]
    [InlineData(1.0, double.PositiveInfinity)]
    public void A_missing_or_unusable_sharpe_is_no_answer(double? strategy, double? hold) => BenchmarkComparison.Beats(strategy, hold).ShouldBeNull();

    [Fact]
    public void The_same_series_on_both_sides_does_not_beat()
    {
        double[] returns = [0.01, -0.02, 0.03, 0.0, 0.015];

        var result = BenchmarkComparison.Of(returns, returns, 5);

        result.Days.ShouldBe(5);
        result.StrategySharpe.ShouldBe(result.HoldSharpe);
        result.Beaten.ShouldBe(false);
        result.Reason.ShouldBeEmpty();
    }

    [Fact]
    public void A_higher_sharpe_beats_and_a_lower_one_does_not()
    {
        double[] hold = [0.01, 0.03, 0.02]; // sharpe 2

        BenchmarkComparison.Of([0.03, 0.01, 0.02], hold, 3).Beaten.ShouldBe(false, "the same three numbers in another order have the same sharpe");
        BenchmarkComparison.Of([0.02, 0.021, 0.019], hold, 3).Beaten.ShouldBe(true, "a steadier series has the higher sharpe");
        BenchmarkComparison.Of([0.01, 0.03, -0.05], hold, 3).Beaten.ShouldBe(false);
    }

    [Fact]
    public void Fewer_days_than_the_minimum_is_unavailable()
    {
        var result = BenchmarkComparison.Of([0.01, 0.02, 0.03], [0.01, 0.03, 0.02], 4);

        result.Beaten.ShouldBeNull();
        result.StrategySharpe.ShouldBeNull();
        result.Reason.ShouldContain("at least 4");
        BenchmarkComparison.Of([0.01, 0.02, 0.03], [0.01, 0.03, 0.02], 3).Beaten.ShouldNotBeNull("exactly the minimum is enough");
    }

    [Fact]
    public void Series_of_different_length_are_unavailable() =>
        BenchmarkComparison.Of([0.01, 0.02, 0.03], [0.01, 0.03], 2).Beaten.ShouldBeNull();

    [Fact]
    public void A_flat_side_makes_the_comparison_unavailable_and_says_which()
    {
        var flatStrategy = BenchmarkComparison.Of([0.0, 0.0, 0.0], [0.01, 0.03, 0.02], 3);
        var flatHold = BenchmarkComparison.Of([0.01, 0.03, 0.02], [0.02, 0.02, 0.02], 3);

        flatStrategy.Beaten.ShouldBeNull();
        flatStrategy.Reason.ShouldContain("strategy");
        flatStrategy.HoldSharpe.ShouldNotBeNull();
        flatHold.Beaten.ShouldBeNull();
        flatHold.Reason.ShouldContain("buy-and-hold");
    }

    [Fact]
    public void The_strategy_value_of_a_day_is_the_sum_of_the_net_r_that_exited_on_it_and_zero_on_a_quiet_day()
    {
        DateOnly[] days = [new(2026, 1, 5), new(2026, 1, 6), new(2026, 1, 7)];

        var daily = BenchmarkComparison.StrategyDaily([(days[0], 1.5), (days[0], -0.5), (days[2], 0.25)], days);

        daily.ShouldBe([1.0, 0.0, 0.25]);
    }

    [Fact]
    public void A_trade_that_exits_outside_the_days_makes_the_series_unavailable()
    {
        DateOnly[] days = [new(2026, 1, 5), new(2026, 1, 6)];

        BenchmarkComparison.StrategyDaily([(new DateOnly(2026, 1, 7), 1.0)], days).ShouldBeNull();
    }
}
