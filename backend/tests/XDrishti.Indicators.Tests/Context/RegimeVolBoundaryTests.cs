using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Context;

/// <summary>
/// The volatility level is decided from exact integers (review of P3-01c, M4): for every window and every pair of percentile ranks, the level is that of the exact fraction — below one third LOW, from two thirds HIGH, one
/// third itself NORMAL — and not of whichever side of 100 / 3 the sum of two rounded percentiles falls on. The expected level is worked out here with integer comparisons only, never with a division.
/// </summary>
public sealed class RegimeVolBoundaryTests
{
    private const int NotRanked = PercentileWindow.NotRanked;

    public static TheoryData<int> Windows { get; } = [1, 2, 3, 4, 6, 9, 12, 30, 252];

    [Theory]
    [MemberData(nameof(Windows))]
    public void The_level_of_every_pair_of_ranks_is_that_of_the_exact_fraction_with_the_default_limits(int window)
    {
        var options = new ContextOptions();
        var misjudgedByTheSumOfRoundedPercentiles = 0;

        for (var index = 1; index < 2 * window; index++)
        {
            // with the index alone the score is index / (2 window) of 100: below a third when 3 index < 2 window, from two thirds when 3 index >= 4 window
            MarketContext.RegimeVol(index, NotRanked, window, options).ShouldBe(index * 3 < 2 * window ? 0 : index * 3 >= 4 * window ? 2 : 1, $"window {window}, index rank {index}, no VIX");

            for (var vix = 1; vix < 2 * window; vix++)
            {
                // with both the score is (index + vix) / (4 window): below a third when 3 (index + vix) < 4 window, from two thirds when 3 (index + vix) >= 8 window
                var sum = index + vix;
                var expected = sum * 3 < 4 * window ? 0.0 : sum * 3 >= 8 * window ? 2.0 : 1.0;

                MarketContext.RegimeVol(index, vix, window, options).ShouldBe(expected, $"window {window}, ranks {index} and {vix}");

                var rounded = (PercentileWindow.Percent(vix, window) + PercentileWindow.Percent(index, window)) / 2.0;
                var byRounded = rounded < options.LowVolBelow ? 0.0 : rounded >= options.HighVolFrom ? 2.0 : 1.0;
                misjudgedByTheSumOfRoundedPercentiles += byRounded == expected ? 0 : 1;
            }
        }

        if (window is 6 or 252)
        {
            // 4 pairs of ranks with a window of 6 and 268 with 252 (counted once with plain doubles): the exact thirds that the mean of two rounded percentiles puts on the wrong side of 100 / 3 or 200 / 3
            misjudgedByTheSumOfRoundedPercentiles.ShouldBeGreaterThan(0, "the test would also pass with the old arithmetic if no pair were misjudged by it");
        }
    }

    [Theory]
    [MemberData(nameof(Windows))]
    public void Limits_that_are_exact_in_binary_are_compared_exactly_and_the_low_limit_is_exclusive_and_the_high_limit_inclusive(int window)
    {
        // 25 and 75 are a quarter and three quarters: the score is below 25 when 100 sum < 25 (4 window), that is sum < window; from 75 when sum >= 3 window
        var options = new ContextOptions(LowVolBelow: 25, HighVolFrom: 75);

        for (var index = 1; index < 2 * window; index++)
        {
            for (var vix = 1; vix < 2 * window; vix++)
            {
                var sum = index + vix;

                MarketContext.RegimeVol(index, vix, window, options).ShouldBe(sum < window ? 0.0 : sum >= 3 * window ? 2.0 : 1.0, $"window {window}, ranks {index} and {vix}");
            }
        }
    }

    [Fact]
    public void A_day_without_an_index_percentile_has_no_level_whatever_the_vix_says()
    {
        MarketContext.RegimeVol(NotRanked, 5, 6, new ContextOptions()).ShouldBeNaN();
        MarketContext.RegimeVol(NotRanked, NotRanked, 6, new ContextOptions()).ShouldBeNaN();
    }

    [Fact]
    public void The_percent_of_a_double_rank_is_the_rank_of_the_definition_and_a_missing_rank_is_not_a_number()
    {
        // 2 below and 1 equal in a window of 4 is 100 * (2 + 0.5) / 4 = 62.5, written as the double rank 5
        PercentileWindow.Percent(5, 4).ShouldBe(62.5);
        PercentileWindow.Percent(1, 4).ShouldBe(12.5, "the lowest value alone: 100 * 0.5 / 4");
        PercentileWindow.Percent(7, 4).ShouldBe(87.5, "the highest value alone: 100 * 3.5 / 4");
        PercentileWindow.Percent(PercentileWindow.NotRanked, 4).ShouldBeNaN();
    }
}
