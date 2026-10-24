using XDrishti.Domain.Planning;

namespace XDrishti.Domain.Tests.Planning;

/// <summary>Spec P7-11: the pure pre-open decision (gap against half the average true range; a missing input never arms), the range and the pinned wording.</summary>
public sealed class PreOpenRulesTests
{
    [Fact]
    public void A_gap_within_the_limit_arms_and_says_the_numbers()
    {
        var decision = PreOpenRules.Decide(new PreOpenReading(101.0, 100.0, 4.0));

        decision.Arm.ShouldBeTrue();
        decision.Code.ShouldBe(PreOpenCodes.GapOk);
        decision.Detail.ShouldBe("gap +1.00 on a close of 100.00, limit 2.00 (0.5 x ATR 4.00)");
    }

    [Theory]
    [InlineData(103.0)]
    [InlineData(97.0)]
    public void A_gap_beyond_half_the_range_in_either_direction_invalidates_with_the_numbers(double last)
    {
        var decision = PreOpenRules.Decide(new PreOpenReading(last, 100.0, 4.0));

        decision.Arm.ShouldBeFalse();
        decision.Code.ShouldBe(PreOpenCodes.GapTooLarge);
        decision.Detail.ShouldContain("limit 2.00");
    }

    [Theory]
    [InlineData(102.0)]
    [InlineData(98.0)]
    public void A_gap_exactly_at_the_limit_still_arms(double last)
    {
        PreOpenRules.Decide(new PreOpenReading(last, 100.0, 4.0)).Arm.ShouldBeTrue();
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
        var decision = PreOpenRules.Decide(new PreOpenReading(last, previous, atr));

        decision.Arm.ShouldBeFalse();
        decision.Code.ShouldBe(code);
    }

    [Fact]
    public void A_wider_limit_arms_the_gap_the_default_refuses()
    {
        var reading = new PreOpenReading(103.0, 100.0, 4.0);

        PreOpenRules.Decide(reading).Arm.ShouldBeFalse();
        PreOpenRules.Decide(reading, 1.0).Arm.ShouldBeTrue();
    }

    [Fact]
    public void The_armed_wording_is_pinned()
    {
        PlanWording.Armed.ShouldBe("Armed: watched with alerts. No order is placed and none exists. A stop is only an alert.");
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
