using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>CF-07, criterion 1 (ADR 0208): the random-entry null by hand on constructed series. The pass rule is the p-value only; the percentiles are displayed.</summary>
public sealed class RandomEntryNullTests
{
    private static readonly NullOptions Options = new();

    /// <summary>1000 seed means in a smooth ramp from -0.1 to 0.1; the hand figures below do not depend on the shape, only on how many are at or above the real mean.</summary>
    private static double[] Ramp(int count = 1000) => [.. Enumerable.Range(0, count).Select(i => -0.1 + (0.2 * i / (count - 1)))];

    /// <summary>A series whose exactly <paramref name="above"/> members are at 1.0 and the rest at 0.0.</summary>
    private static double[] WithAbove(int above) => [.. Enumerable.Range(0, 1000).Select(i => i < above ? 1.0 : 0.0)];

    [Fact]
    public void Thirty_eight_of_one_thousand_seed_means_at_or_above_the_real_mean_is_p_39_over_1001_and_passes()
    {
        var result = RandomEntryNull.Evaluate(0.5, 320, 1000, WithAbove(38), 0, Options);

        result.CountAtLeast.ShouldBe(38);
        result.PValue!.Value.ShouldBe(39.0 / 1001.0, 1e-12);
        result.PValue!.Value.ShouldBe(0.0390, 1e-4);
        result.Threshold.ShouldBe(0.05);
        result.Status.ShouldBe(NullStatus.Pass);
    }

    [Fact]
    public void Fifty_of_one_thousand_is_p_51_over_1001_and_fails()
    {
        var result = RandomEntryNull.Evaluate(0.5, 320, 1000, WithAbove(50), 0, Options);

        result.PValue!.Value.ShouldBe(51.0 / 1001.0, 1e-12);
        result.PValue!.Value.ShouldBe(0.0509, 1e-4);
        result.Status.ShouldBe(NullStatus.Fail);
    }

    [Fact]
    public void A_mean_equal_to_the_real_mean_counts_as_at_or_above()
    {
        var result = RandomEntryNull.Evaluate(1.0, 320, 1000, WithAbove(10), 0, Options);

        result.CountAtLeast.ShouldBe(10);
    }

    [Fact]
    public void The_percentiles_are_the_empirical_ones_of_the_seed_means_and_the_95th_is_displayed_not_a_second_test()
    {
        var means = Ramp();

        var result = RandomEntryNull.Evaluate(0.05, 320, 1000, means, 0, Options);

        result.P95!.Value.ShouldBe(-0.1 + (0.2 * 949 / 999), 1e-9, "the 95th percentile of the ramp, nearest rank");
        result.P05!.Value.ShouldBe(-0.1 + (0.2 * 50 / 999), 1e-9);
        result.P50!.Value.ShouldBe(-0.1 + (0.2 * 500 / 999), 1e-9);
        result.P99!.Value.ShouldBe(-0.1 + (0.2 * 989 / 999), 1e-9);
        result.RandomMeanR!.Value.ShouldBe(0.0, 1e-9);
        result.PercentileLevel.ShouldBe(95);
        result.PercentileValue.ShouldBe(result.P95);

        // 250 of the 1000 ramp values are at or above 0.05: p = 251 / 1001
        result.CountAtLeast.ShouldBe(250);
        result.PValue!.Value.ShouldBe(251.0 / 1001.0, 1e-12);
        result.Status.ShouldBe(NullStatus.Fail);
    }

    [Fact]
    public void A_real_mean_of_0_12_passes_against_seed_means_of_mean_zero_and_fails_against_mean_0_07_with_sd_0_04()
    {
        // exact normal construction: the 1000 seed means sit at the plotting positions (i + 0.5) / 1000 of a normal with sd 0.04
        double[] Series(double mean) => [.. Enumerable.Range(0, 1000).Select(i => mean + (0.04 * NormalDistribution.Quantile((i + 0.5) / 1000.0)))];

        var pass = RandomEntryNull.Evaluate(0.12, 320, 1000, Series(0), 0, Options);
        var fail = RandomEntryNull.Evaluate(0.12, 320, 1000, Series(0.07), 0, Options);

        pass.P95!.Value.ShouldBe(0.0658, 0.002, "1.645 x 0.04");
        pass.Status.ShouldBe(NullStatus.Pass);
        fail.P95!.Value.ShouldBe(0.1358, 0.002, "0.07 + 1.645 x 0.04");
        fail.RealMeanR.ShouldBeLessThan(fail.P95!.Value);
        fail.Status.ShouldBe(NullStatus.Fail);
    }

    [Fact]
    public void The_level_follows_the_configured_percentile()
    {
        var strict = RandomEntryNull.Evaluate(0.5, 320, 1000, WithAbove(38), 0, Options with { Percentile = 99 });

        strict.Threshold.ShouldBe(0.01);
        strict.Status.ShouldBe(NullStatus.Fail, "p = 0.039 is not at most 0.01");
    }

    [Fact]
    public void The_result_is_the_same_for_the_same_inputs_in_any_order_of_the_seeds()
    {
        var means = Ramp();

        var forward = RandomEntryNull.Evaluate(0.01, 320, 1000, means, 0, Options);
        var backward = RandomEntryNull.Evaluate(0.01, 320, 1000, [.. means.Reverse()], 0, Options);

        backward.ShouldBe(forward);
    }

    [Fact]
    public void Too_few_trades_is_unavailable_and_never_a_pass()
    {
        var result = RandomEntryNull.Evaluate(0.9, RandomEntryNull.MinTrades - 1, 1000, WithAbove(0), 0, Options);

        result.Status.ShouldBe(NullStatus.Unavailable);
        result.PValue.ShouldBeNull();
        result.P95.ShouldBeNull();
        result.Reason.ShouldStartWith("unavailable:");
    }

    [Fact]
    public void A_large_dropped_share_or_a_non_finite_number_is_unavailable()
    {
        RandomEntryNull.Evaluate(0.9, 320, 1000, Ramp(), 0.21, Options).Status.ShouldBe(NullStatus.Unavailable);
        RandomEntryNull.Evaluate(0.9, 320, 1000, Ramp(), double.NaN, Options).Status.ShouldBe(NullStatus.Unavailable);
        RandomEntryNull.Evaluate(double.NaN, 320, 1000, Ramp(), 0, Options).Status.ShouldBe(NullStatus.Unavailable);
        RandomEntryNull.Evaluate(0.9, 320, 1000, [.. Ramp().Append(double.PositiveInfinity)], 0, Options).Status.ShouldBe(NullStatus.Unavailable);
        RandomEntryNull.Evaluate(0.9, 320, 1000, Ramp(), 0.2, Options).Status.ShouldBe(NullStatus.Pass, "a dropped share exactly at the limit is allowed");
    }
}
