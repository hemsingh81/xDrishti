using XDrishti.Indicators.Tests.Support;

namespace XDrishti.Indicators.Tests.Context;

/// <summary>
/// Hand-calculated examples of the market context (criterion 8), each at most twelve days with small periods: the trend rule up, down and range, the slope distance, the percentile mid-rank with ties for the VIX
/// and for the index's ATR, the volatility terciles and their boundaries, the VIX-missing fallback, the breadth, and the nine regime codes. The numbers in the comments are worked out on paper.
/// </summary>
public sealed class ContextHandTests
{
    /// <summary>Average of 3 days, slope over 2, ADX(2) (ready at day 4), ATR(2), percentile window of 4: the trend is ready at day 5, the index percentile at day 5, the VIX percentile at the 4th VIX.</summary>
    private static readonly ContextOptions Small = new(TrendSmaPeriod: 3, TrendSlopeDays: 2, AdxPeriod: 2, AdxMin: 20, AtrPeriod: 2, PercentileWindow: 4);

    private static readonly double NaN = double.NaN;

    [Fact]
    public void A_straight_climb_is_an_uptrend_from_day_five_with_an_adx_of_one_hundred()
    {
        // closes 100, 102, ... 114 with high = close + 1, low = close - 1: every true range is 3 (from day 2), every +DM is 2, no -DM -> +DI 66.7, -DI 0, DX 100, ADX(2) = 100 at day 4.
        // average of 3: 102 (day 3), 104, 106, 108 ...; the average of day 5 (106) is above that of day 3 (102); the close 108 is above 106.
        var days = Straight(100, 2, 8);

        var values = Read(days, Small);

        values.Select(v => v.IndexAdx).Take(3).ShouldAllBe(v => double.IsNaN(v));
        values.Skip(3).ShouldAllBe(v => Math.Abs(v.IndexAdx - 100) < 1e-9);
        values.Select(v => v.IndexTrend).ShouldBe([NaN, NaN, NaN, NaN, 1, 1, 1, 1]);
        values.Select(v => v.RegimeTrend).ShouldBe([NaN, NaN, NaN, NaN, 1, 1, 1, 1]);
    }

    [Fact]
    public void A_straight_fall_is_a_downtrend_from_day_five()
    {
        // the mirror image: closes 120, 118, ... 106; the average (118 at day 3, 114 at day 5) falls and the close is below it; -DM 2 each day, ADX 100
        var days = Straight(120, -2, 8);

        var values = Read(days, Small);

        values.Skip(3).ShouldAllBe(v => Math.Abs(v.IndexAdx - 100) < 1e-9);
        values.Select(v => v.IndexTrend).ShouldBe([NaN, NaN, NaN, NaN, -1, -1, -1, -1]);
    }

    [Fact]
    public void A_rising_average_with_a_weak_adx_is_a_range()
    {
        // every bar is wider than the one before on both sides (high + 1, low - 1): up = down = 1, neither is the larger, so +DM = -DM = 0, DX = 0 and ADX = 0, although
        // the closes 100, 100.5, ... rise: the average (100.5 at day 3, 101.5 at day 5) rises and the close is above it.
        var days = Enumerable.Range(0, 6).Select(i => new ContextDay(Weekday(i), 101 + i, 99 - i, 100 + 0.5 * i)).ToList();

        var values = Read(days, Small);

        values.Skip(3).ShouldAllBe(v => v.IndexAdx == 0);
        values.Select(v => v.IndexTrend).ShouldBe([NaN, NaN, NaN, NaN, 0, 0]);
    }

    [Fact]
    public void The_adx_limit_is_inclusive_so_an_adx_of_zero_passes_a_limit_of_zero()
    {
        var days = Enumerable.Range(0, 6).Select(i => new ContextDay(Weekday(i), 101 + i, 99 - i, 100 + 0.5 * i)).ToList();

        var values = Read(days, Small with { AdxMin = 0 });

        values.Select(v => v.IndexTrend).ShouldBe([NaN, NaN, NaN, NaN, 1, 1]);
    }

    [Fact]
    public void A_close_below_a_rising_average_is_a_range_even_when_the_adx_is_high()
    {
        // closes 100, 102, 104, 106, 108 as above, then day 6: high 109, low 99, close 100. Day 6: up = 0, down = 107 - 99 = 8 -> -DM 8, +DM 0; TR = max(10, 1, 9) = 10.
        // Wilder sums (n = 2): TR 6 - 3 + 10 = 13, +DM 4 - 2 + 0 = 2, -DM 0 - 0 + 8 = 8 -> +DI 15.38, -DI 61.54, DX = 100 * 6 / 10 = 60, ADX = (100 + 60) / 2 = 80.
        // The average of day 6 is (106 + 108 + 100) / 3 = 104.67 > 104 (day 4): the slope is positive, but the close 100 is below the average -> neither up nor down.
        var days = Straight(100, 2, 5);
        days.Add(new ContextDay(Weekday(5), 109, 99, 100));

        var values = Read(days, Small);

        values[4].IndexTrend.ShouldBe(1);
        values[5].IndexAdx.ShouldBe(80, 1e-9);
        values[5].IndexTrend.ShouldBe(0);
    }

    [Fact]
    public void The_slope_is_measured_over_the_configured_number_of_days()
    {
        // average of 2, ADX(1) with a limit of 0 so that only the average decides. Closes 1, 10, 8, 9: the averages are 5.5 (day 2), 9 (day 3) and 8.5 (day 4).
        // Over 2 days the average rose (8.5 against 5.5) and the close 9 is above 8.5 -> up. Over 1 day it fell (8.5 against 9): the close is above the average but the slope is negative -> range.
        var days = new[] { 1.0, 10, 8, 9 }.Select((c, i) => new ContextDay(Weekday(i), c + 0.5, c - 0.5, c)).ToList();
        var options = new ContextOptions(TrendSmaPeriod: 2, TrendSlopeDays: 2, AdxPeriod: 1, AdxMin: 0, AtrPeriod: 1, PercentileWindow: 4);

        Read(days, options)[3].IndexTrend.ShouldBe(1);
        Read(days, options with { TrendSlopeDays = 1 })[3].IndexTrend.ShouldBe(0);
    }

    [Theory]
    [InlineData(10, 10, 8, 13, 1)]      // average 10 -> 10.5, close 13 above it
    [InlineData(10, 10, 12, 7, -1)]     // average 10 -> 9.5, close 7 below it
    [InlineData(10, 10, 8, 12, 0)]      // average 10 -> 10: no slope, although the close 12 is above it
    [InlineData(10, 10, 12, 8, 0)]      // average 10 -> 10: no slope, although the close 8 is below it
    [InlineData(1, 2, 10, 10, 0)]       // average 1.5 -> 10 rises, but the close 10 is exactly on the average (not above)
    [InlineData(20, 19, 10, 10, 0)]     // average 19.5 -> 10 falls, but the close 10 is exactly on the average (not below)
    public void Above_or_below_means_strictly_and_a_slope_of_zero_is_a_range(double c1, double c2, double c3, double c4, double expected)
    {
        // average of 2, slope over 2 days (day 2 against day 4), ADX limit 0 so that only the average decides
        var days = new[] { c1, c2, c3, c4 }.Select((c, i) => new ContextDay(Weekday(i), c + 0.5, c - 0.5, c)).ToList();
        var options = new ContextOptions(TrendSmaPeriod: 2, TrendSlopeDays: 2, AdxPeriod: 1, AdxMin: 0, AtrPeriod: 1, PercentileWindow: 4);

        Read(days, options)[3].IndexTrend.ShouldBe(expected);
    }

    [Fact]
    public void The_trend_is_not_ready_until_the_average_its_slope_and_the_adx_all_exist()
    {
        // average 3 + slope 2 -> day 5; but with ADX(4) (ready at day 8) the trend waits for the ADX
        var days = Straight(100, 2, 10);

        Read(days, Small)[3].IndexTrend.ShouldBeNaN();
        Read(days, Small)[4].IndexTrend.ShouldBe(1);
        Read(days, Small with { AdxPeriod = 4 }).Select(v => v.IndexTrend).ShouldBe([NaN, NaN, NaN, NaN, NaN, NaN, NaN, 1, 1, 1]);
    }

    [Fact]
    public void The_vix_percentile_ranks_ties_at_the_middle_of_their_span()
    {
        // window 4. Day 4: [15 20 20 10], 10 is below none and equals itself: 100 * 0.5 / 4 = 12.5. Day 5: [20 20 10 20], 20 has one below and three equal: 100 * (1 + 1.5) / 4 = 62.5.
        // Day 6: [20 10 20 15], 15: one below, itself: 37.5. Day 7: [10 20 15 30], the highest: 100 * 3.5 / 4 = 87.5. Day 8: [20 15 30 30]: two below, two equal: 100 * 3 / 4 = 75.
        // Day 9: [15 30 30 30]: 62.5. Day 10: [30 30 30 30]: all equal -> 100 * 2 / 4 = 50.
        var days = FlatIndex(15, 20, 20, 10, 20, 15, 30, 30, 30, 30);

        var values = Read(days, Small);

        values.Select(v => v.VixPct).ShouldBe([NaN, NaN, NaN, 12.5, 62.5, 37.5, 87.5, 75, 62.5, 50]);
        values.Select(v => v.Vix).ShouldBe([15, 20, 20, 10, 20, 15, 30, 30, 30, 30]);
    }

    [Fact]
    public void The_vix_change_is_the_percent_move_against_the_previous_day()
    {
        var values = Read(FlatIndex(10, 12, 12, 9, 18), Small);

        // +20 %, 0 %, -25 %, +100 %
        Matches(values.Select(v => v.VixChg), [NaN, 20, 0, -25, 100]);
    }

    [Fact]
    public void The_index_volatility_percentile_ranks_the_atr_as_a_percent_of_the_close_with_the_same_mid_rank()
    {
        // ATR(1) is the true range; with every close at 200 and the high / low r above / below it the true range is 2r and the ATR percent is r (r = 1, 2, 2, 0.5, 2, 1, 3, 3).
        // Window 4: day 4 [1 2 2 0.5] -> 12.5; day 5 [2 2 0.5 2] -> 62.5; day 6 [2 0.5 2 1] -> 37.5; day 7 [0.5 2 1 3] -> 87.5; day 8 [2 1 3 3] -> 75.
        var half = new[] { 1.0, 2, 2, 0.5, 2, 1, 3, 3 };
        var days = half.Select((r, i) => new ContextDay(Weekday(i), 200 + r, 200 - r, 200)).ToList();
        var options = new ContextOptions(TrendSmaPeriod: 3, TrendSlopeDays: 2, AdxPeriod: 2, AdxMin: 20, AtrPeriod: 1, PercentileWindow: 4);

        var values = Read(days, options);

        Matches(values.Select(v => v.IndexVolPct), [NaN, NaN, NaN, 12.5, 62.5, 37.5, 87.5, 75]);
    }

    [Fact]
    public void The_index_volatility_of_a_flat_range_ranks_fifty_once_the_window_is_full()
    {
        // high 101, low 99, close 100 every day: TR 2 (ATR(2) = 2 from day 2), ATR percent 2; four equal values from day 5 -> 100 * 2 / 4
        var values = Read(FlatIndex(null, null, null, null, null, null), Small);

        values.Select(v => v.IndexVolPct).ShouldBe([NaN, NaN, NaN, NaN, 50, 50]);
    }

    [Theory]
    [InlineData(30, 30, 30, 20, 0)]
    [InlineData(20, 20, 20, 20, 1)]
    [InlineData(10, 10, 10, 20, 2)]
    public void The_volatility_level_is_the_tercile_of_the_mean_of_the_two_percentiles(double vix1, double vix2, double vix3, double vix4, double expected)
    {
        // a flat index ranks 50 from day 5; the VIX window of day 5 is the four values given (day 1 carries a VIX of its own, outside the window):
        // [30 30 30 20]: 20 is the lowest -> 12.5, score (12.5 + 50) / 2 = 31.25 < 33.33 -> LOW (0)
        // [20 20 20 20]: all equal -> 50, score 50 -> NORMAL (1)
        // [10 10 10 20]: 20 is the highest -> 87.5, score 68.75 >= 66.67 -> HIGH (2)
        var values = Read(FlatIndex(15, vix1, vix2, vix3, vix4), Small);

        values[4].IndexVolPct.ShouldBe(50);
        values[4].RegimeVol.ShouldBe(expected);
    }

    [Fact]
    public void The_low_limit_is_exclusive_and_the_high_limit_inclusive()
    {
        // the scores are exactly 31.25 (VIX [30 30 30 20]) and exactly 68.75 (VIX [10 10 10 20]) with a flat index
        var low = FlatIndex(15, 30, 30, 30, 20);
        var high = FlatIndex(15, 10, 10, 10, 20);

        Read(low, Small with { LowVolBelow = 31.25 })[4].RegimeVol.ShouldBe(1, "31.25 is not below 31.25");
        Read(low, Small with { LowVolBelow = 31.5 })[4].RegimeVol.ShouldBe(0);
        Read(high, Small with { HighVolFrom = 68.75 })[4].RegimeVol.ShouldBe(2, "68.75 is from 68.75");
        Read(high, Small with { HighVolFrom = 68.8 })[4].RegimeVol.ShouldBe(1);
    }

    [Fact]
    public void A_score_of_exactly_two_thirds_is_high_although_the_two_percentiles_are_not_whole_numbers()
    {
        // window 6, a flat index: every ATR percent is 2, six equal values rank 100 * 3 / 6 = 50. VIX days 2 to 7 are [10 10 10 10 20 20] (day 1 carries a VIX of its own, outside the window):
        // the newest, 20, has four below and two equal (itself and one more): 100 * (4 + 1) / 6 = 83.33. The score (83.33 + 50) / 2 is exactly two thirds of 100, and "from two thirds" is HIGH.
        // In floating point 100 * 5 / 6 is 83.33333333333333, plus 50, halved, is 66.66666666666666: one step below the double that stands for 200 / 3, so a sum of the two rounded percentiles said NORMAL.
        var options = Small with { PercentileWindow = 6 };

        var values = Read(FlatIndex(15, 10, 10, 10, 10, 20, 20), options);

        values[6].IndexVolPct.ShouldBe(50);
        values[6].VixPct.ShouldBe(100.0 * 5 / 6, 1e-12);
        values[6].RegimeVol.ShouldBe(2, "exactly two thirds is from two thirds");
    }

    [Fact]
    public void A_score_of_exactly_one_third_is_normal_although_the_two_percentiles_are_not_whole_numbers()
    {
        // window 6, ATR(1) = true range, close 200 and a range of r each side: the ATR percent is r. The index days are r = 5 5 4 1 1 1: the newest, 1, has none below and three equal: 100 * 1.5 / 6 = 25.
        // VIX days are 20 10 10 10 10 10: the newest, 10, has none below and five equal: 100 * 2.5 / 6 = 41.67. The score (25 + 41.67) / 2 is exactly one third of 100, and "below one third" is LOW, so one third itself is NORMAL.
        // In floating point 41.666666666666664 + 25 is 66.66666666666666 and half of it 33.33333333333333, one step below the double that stands for 100 / 3: the sum of the two rounded percentiles said LOW.
        var half = new[] { 5.0, 5, 4, 1, 1, 1 };
        var vix = new double?[] { 20, 10, 10, 10, 10, 10 };
        var days = half.Select((r, i) => new ContextDay(Weekday(i), 200 + r, 200 - r, 200, vix[i])).ToList();
        var options = new ContextOptions(TrendSmaPeriod: 3, TrendSlopeDays: 2, AdxPeriod: 2, AdxMin: 20, AtrPeriod: 1, PercentileWindow: 6);

        var values = Read(days, options);

        values[5].IndexVolPct.ShouldBe(25);
        values[5].VixPct.ShouldBe(100.0 * 2.5 / 6, 1e-12);
        values[5].RegimeVol.ShouldBe(1, "exactly one third is not below one third");
    }

    [Fact]
    public void Without_a_vix_the_volatility_falls_back_to_the_index_percentile_alone()
    {
        // VIX [10 10 10 20] on days 2 to 5, none on day 6, 25 and 20 on days 7 and 8; the index percentile is 50 from day 5.
        // Day 5: score (87.5 + 50) / 2 = 68.75 -> HIGH. Day 6 has no VIX: nothing is carried forward, the VIX values are NaN and the score is the index percentile, 50 -> NORMAL.
        var values = Read(FlatIndex(10, 10, 10, 10, 20, null, 25, 20), Small);

        values[4].VixPct.ShouldBe(87.5);
        values[4].RegimeVol.ShouldBe(2);
        values[5].Vix.ShouldBeNaN();
        values[5].VixPct.ShouldBeNaN();
        values[5].VixChg.ShouldBeNaN();
        values[5].IndexVolPct.ShouldBe(50);
        values[5].RegimeVol.ShouldBe(1);
    }

    [Fact]
    public void A_vix_gap_is_not_filled_the_next_change_is_not_ready_and_the_percentile_window_counts_only_days_with_a_vix()
    {
        // VIX days: 10 (1), 10 (2), 10 (3), 10 (4), 20 (5), - (6), 25 (7), 20 (8).
        // Day 7 follows the gap: the change needs the previous day of the context, which has no VIX -> NaN (not 25 against 20). Its window is the last four VIX days [10 10 20 25]: 25 is the highest -> 87.5.
        // Day 8: change (20 - 25) / 25 = -20 %; window [10 20 25 20]: one below, two equal -> 100 * 2 / 4 = 50.
        var values = Read(FlatIndex(10, 10, 10, 10, 20, null, 25, 20), Small);

        values[4].VixChg.ShouldBe(100, 1e-12);
        values[6].Vix.ShouldBe(25);
        values[6].VixChg.ShouldBeNaN();
        values[6].VixPct.ShouldBe(87.5);
        values[7].VixChg.ShouldBe(-20, 1e-12);
        values[7].VixPct.ShouldBe(50);
    }

    [Fact]
    public void A_vix_that_starts_late_gives_the_percentile_only_after_a_full_window_of_vix_days()
    {
        var values = Read(FlatIndex(null, null, 12, 13, 14, 15, 16), Small);

        values.Select(v => v.VixPct).ShouldBe([NaN, NaN, NaN, NaN, NaN, 87.5, 87.5]);
        values.Select(v => v.Vix).ShouldBe([NaN, NaN, 12, 13, 14, 15, 16]);
    }

    [Fact]
    public void While_the_vix_history_is_shorter_than_the_window_the_volatility_level_comes_from_the_index_percentile_alone()
    {
        // Amendment 2: the index percentile alone is the score on any day where the VIX percentile is not ready, also when the VIX only started recently. Window 4, a flat index (percentile 50 from day 5).
        // The VIX starts on day 3 (12 13 14 15 16): on day 5 there are three VIX days, so the VIX percentile is not ready and the score is 50 -> NORMAL, and the regime (range, normal) is 5.
        // On day 6 the window [12 13 14 15] is full: 15 is the highest -> 87.5, the score (87.5 + 50) / 2 = 68.75 -> HIGH, the regime is 6 (range, high).
        var values = Read(FlatIndex(null, null, 12, 13, 14, 15, 16), Small);

        values[4].VixPct.ShouldBeNaN();
        values[4].IndexVolPct.ShouldBe(50);
        values[4].RegimeVol.ShouldBe(1);
        values[4].Regime.ShouldBe(5);
        values[5].VixPct.ShouldBe(87.5);
        values[5].RegimeVol.ShouldBe(2);
        values[5].Regime.ShouldBe(6);
    }

    [Fact]
    public void The_breadth_is_the_value_of_the_day_and_not_ready_when_the_day_has_none()
    {
        var days = new List<ContextDay>
        {
            new(Weekday(0), 101, 99, 100, null, 0),
            new(Weekday(1), 101, 99, 100, null, 62.5),
            new(Weekday(2), 101, 99, 100),
            new(Weekday(3), 101, 99, 100, null, 100),
        };

        Read(days, Small).Select(v => v.Breadth).ShouldBe([0, 62.5, NaN, 100]);
    }

    [Theory]
    [InlineData("up", "low", 1, "UP_LOW_VOL")]
    [InlineData("up", "normal", 2, "UP_NORMAL_VOL")]
    [InlineData("up", "high", 3, "UP_HIGH_VOL")]
    [InlineData("range", "low", 4, "RANGE_LOW_VOL")]
    [InlineData("range", "normal", 5, "RANGE_NORMAL_VOL")]
    [InlineData("range", "high", 6, "RANGE_HIGH_VOL")]
    [InlineData("down", "low", 7, "DOWN_LOW_VOL")]
    [InlineData("down", "normal", 8, "DOWN_NORMAL_VOL")]
    [InlineData("down", "high", 9, "DOWN_HIGH_VOL")]
    public void The_nine_regimes_are_the_trend_times_the_volatility_level(string trend, string volatility, int code, string name)
    {
        // the trend comes from the series (straight climb, straight fall, or the widening bars whose ADX is 0); the volatility level from the limits: with the low limit at 100 every score (at most 87.5)
        // is low; with both limits at 0 every score is high; with the limits 0 and 100 every score is normal
        var days = trend switch
        {
            "up" => Straight(100, 2, 6),
            "down" => Straight(120, -2, 6),
            _ => Enumerable.Range(0, 6).Select(i => new ContextDay(Weekday(i), 101 + i, 99 - i, 100 + 0.5 * i)).ToList(),
        };
        var options = volatility switch
        {
            "low" => Small with { LowVolBelow = 100, HighVolFrom = 100 },
            "high" => Small with { LowVolBelow = 0, HighVolFrom = 0 },
            _ => Small with { LowVolBelow = 0, HighVolFrom = 100 },
        };

        var day = Read(days, options)[4];

        day.Regime.ShouldBe(code);
        Regime.Name((int)day.Regime).ShouldBe(name);
        day.RegimeTrend.ShouldBe(trend == "up" ? 1 : trend == "down" ? -1 : 0);
        day.RegimeVol.ShouldBe(volatility == "low" ? 0 : volatility == "normal" ? 1 : 2);
    }

    [Fact]
    public void The_regime_is_not_ready_unless_the_trend_and_the_volatility_both_are()
    {
        // the index percentile (window 4 of ATR(2) values) exists from day 5; the trend from day 5 as well. With a window of 6 the volatility waits until day 7
        var values = Read(Straight(100, 2, 8), Small with { PercentileWindow = 6 });

        values.Select(v => v.RegimeTrend).ShouldBe([NaN, NaN, NaN, NaN, 1, 1, 1, 1]);
        values.Select(v => v.RegimeVol).Take(6).ShouldAllBe(v => double.IsNaN(v));
        values.Select(v => v.Regime).Take(6).ShouldAllBe(v => double.IsNaN(v));
        double.IsNaN(values[6].Regime).ShouldBeFalse();
        double.IsNaN(values[7].Regime).ShouldBeFalse();
    }

    /// <summary>The values equal the expected ones within the project tolerance (NaN equals NaN: not ready on both sides).</summary>
    private static void Matches(IEnumerable<double> actual, double[] expected)
    {
        var values = actual.ToArray();
        values.Length.ShouldBe(expected.Length);
        for (var i = 0; i < values.Length; i++)
        {
            Tolerance.Same(expected[i], values[i]).ShouldBeTrue($"day {i + 1}: expected {expected[i]:R} but was {values[i]:R}");
        }
    }

    private static List<ContextValues> Read(List<ContextDay> days, ContextOptions options) =>
        ContextRead.Days(MarketContext.Create(days, options).Value, days);

    private static DateOnly Weekday(int n) => ContextSeriesGenerator.Weekday(n);

    /// <summary>Consecutive weekdays of an index that climbs (or falls) by <paramref name="step"/> a day with a range of one point each side of the close.</summary>
    private static List<ContextDay> Straight(double first, double step, int count) =>
        [.. Enumerable.Range(0, count).Select(i => new ContextDay(Weekday(i), first + step * i + 1, first + step * i - 1, first + step * i))];

    /// <summary>An index that does not move (high 101, low 99, close 100) with the given VIX closes (null: none that day).</summary>
    private static List<ContextDay> FlatIndex(params double?[] vix) =>
        [.. vix.Select((v, i) => new ContextDay(Weekday(i), 101, 99, 100, v))];
}
