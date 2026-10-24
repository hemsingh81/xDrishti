using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.Planning;

/// <summary>Spec P7-11: the pure pre-open decision (gap against half the average true range; a missing input, a stale or untimed price never arms), the stale-batch guard, the range and the pinned wording.</summary>
public sealed class PreOpenRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 26);

    /// <summary>09:10 India time on <see cref="Today"/>.</summary>
    private static readonly DateTimeOffset PreOpen = new(2026, 10, 26, 3, 40, 0, TimeSpan.Zero);

    private static PreOpenReading Reading(double? last, double? previous, double? atr, DateTimeOffset? at = null) => new(last, previous, atr, at ?? PreOpen);

    [Fact]
    public void A_gap_within_the_limit_arms_and_says_the_numbers()
    {
        var decision = PreOpenRules.Decide(Reading(101.0, 100.0, 4.0), Today);

        decision.Arm.ShouldBeTrue();
        decision.Code.ShouldBe(PreOpenCodes.GapOk);
        decision.Detail.ShouldBe("gap +1.00 on a close of 100.00, limit 2.00 (0.5 x ATR 4.00)");
    }

    [Theory]
    [InlineData(103.0)]
    [InlineData(97.0)]
    public void A_gap_beyond_half_the_range_in_either_direction_invalidates_with_the_numbers(double last)
    {
        var decision = PreOpenRules.Decide(Reading(last, 100.0, 4.0), Today);

        decision.Arm.ShouldBeFalse();
        decision.Code.ShouldBe(PreOpenCodes.GapTooLarge);
        decision.Detail.ShouldContain("limit 2.00");
    }

    [Theory]
    [InlineData(102.0)]
    [InlineData(98.0)]
    public void A_gap_exactly_at_the_limit_still_arms(double last)
    {
        PreOpenRules.Decide(Reading(last, 100.0, 4.0), Today).Arm.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null, 100.0, 4.0, PreOpenCodes.UnavailableQuote)]
    [InlineData(0.0, 100.0, 4.0, PreOpenCodes.UnavailableQuote)]
    [InlineData(double.NaN, 100.0, 4.0, PreOpenCodes.UnavailableQuote)]
    [InlineData(100.0, null, 4.0, PreOpenCodes.UnavailablePreviousClose)]
    [InlineData(100.0, -1.0, 4.0, PreOpenCodes.UnavailablePreviousClose)]
    [InlineData(100.0, 100.0, null, PreOpenCodes.UnavailableAtr)]
    [InlineData(100.0, 100.0, 0.0, PreOpenCodes.UnavailableAtr)]
    public void A_missing_or_unusable_input_invalidates_and_never_arms(double? last, double? previous, double? atr, string code)
    {
        var decision = PreOpenRules.Decide(Reading(last, previous, atr), Today);

        decision.Arm.ShouldBeFalse();
        decision.Code.ShouldBe(code);
    }

    [Fact]
    public void A_price_with_no_trade_time_reads_as_no_price()
    {
        var decision = PreOpenRules.Decide(new PreOpenReading(101.0, 100.0, 4.0), Today);

        decision.Arm.ShouldBeFalse();
        decision.Code.ShouldBe(PreOpenCodes.UnavailableQuote);
        decision.Detail.ShouldContain("no trade time");
    }

    [Theory]
    [InlineData(2026, 10, 23, 9, 55, 0, "yesterday's trade")]
    [InlineData(2026, 10, 26, 8, 59, 59, "before the pre-open session")]
    [InlineData(2026, 10, 26, 9, 15, 0, "at the open")]
    [InlineData(2026, 10, 26, 10, 0, 0, "after the open")]
    [InlineData(2026, 10, 27, 9, 10, 0, "tomorrow")]
    public void A_trade_time_that_is_not_in_todays_pre_open_session_reads_as_no_price(int year, int month, int day, int hour, int minute, int second, string why)
    {
        var at = new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromMinutes(330));

        var decision = PreOpenRules.Decide(Reading(101.0, 100.0, 4.0, at), Today);

        decision.Arm.ShouldBeFalse(why);
        decision.Code.ShouldBe(PreOpenCodes.UnavailableQuote);
    }

    [Theory]
    [InlineData(9, 0, 0)]
    [InlineData(9, 7, 59)]
    [InlineData(9, 14, 59)]
    public void A_trade_time_inside_the_pre_open_session_of_today_is_accepted(int hour, int minute, int second)
    {
        var at = new DateTimeOffset(2026, 10, 26, hour, minute, second, TimeSpan.FromMinutes(330));

        PreOpenRules.Decide(Reading(101.0, 100.0, 4.0, at), Today).Arm.ShouldBeTrue();
    }

    [Fact]
    public void A_wider_limit_arms_the_gap_the_default_refuses()
    {
        var reading = Reading(103.0, 100.0, 4.0);

        PreOpenRules.Decide(reading, Today).Arm.ShouldBeFalse();
        PreOpenRules.Decide(reading, Today, 1.0).Arm.ShouldBeTrue();
    }

    [Theory]
    [InlineData(4, 4, true)]
    [InlineData(5, 4, true)]
    [InlineData(5, 3, false)]
    [InlineData(10, 8, true)]
    [InlineData(10, 7, false)]
    [InlineData(2, 2, false)]
    [InlineData(0, 0, false)]
    public void A_batch_where_most_prices_equal_the_previous_close_is_stale_only_from_three_readings(int total, int equal, bool stale)
    {
        var batch = Enumerable.Range(0, total).Select(i => Reading(i < equal ? 100.0 : 101.0, 100.0, 4.0)).ToList();

        PreOpenRules.StaleBatch(batch, out var counted, out var size).ShouldBe(stale);
        counted.ShouldBe(equal);
        size.ShouldBe(total);
    }

    [Fact]
    public void Readings_without_a_price_or_a_previous_close_do_not_count_in_the_batch()
    {
        var batch = new[] { Reading(100.0, 100.0, 4.0), Reading(100.0, 100.0, 4.0), Reading(null, 100.0, 4.0), Reading(100.0, null, 4.0) };

        PreOpenRules.StaleBatch(batch, out var equal, out var total).ShouldBeFalse();
        (equal, total).ShouldBe((2, 2));
    }

    [Fact]
    public void The_armed_wording_is_pinned()
    {
        PlanWording.Armed.ShouldBe("Armed: watched with alerts. No order is placed and none exists. A stop is only an alert.");
        PlanWording.AlertsNotBuilt.ShouldBe("Price alerts on trigger and stop are not built yet; only the live price is shown.");
        PlanWording.PreOpenPriceCaveat.ShouldBe("Last price may be the previous close before the open.");
    }

    [Fact]
    public void The_range_is_the_mean_of_the_last_true_ranges_and_needs_enough_sessions()
    {
        var sessions = Enumerable.Range(0, 15).Select(i => new DailyRange(102.0, 98.0, 100.0)).ToList();

        AverageTrueRange.Of(sessions).ShouldBe(4.0);
        AverageTrueRange.Of(sessions.Take(14).ToList()).ShouldBeNull();
    }

    [Fact]
    public void A_gap_between_sessions_counts_in_the_true_range()
    {
        var sessions = Enumerable.Range(0, 14).Select(i => new DailyRange(102.0, 98.0, 100.0)).Append(new DailyRange(112.0, 110.0, 111.0)).ToList();

        // Thirteen ranges of 4 and one of max(2, |112-100|, |110-100|) = 12.
        AverageTrueRange.Of(sessions).ShouldNotBeNull().ShouldBe((13 * 4.0 + 12.0) / 14, 1e-9);
    }

    [Fact]
    public void A_bad_bar_gives_no_range()
    {
        var sessions = Enumerable.Range(0, 14).Select(i => new DailyRange(102.0, 98.0, 100.0)).Append(new DailyRange(90.0, 95.0, 92.0)).ToList();

        AverageTrueRange.Of(sessions).ShouldBeNull();
    }
}
