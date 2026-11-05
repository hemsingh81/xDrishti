using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-12 part 12a, AC10 (ADR 0218, spec D6): the reliability slope by oracle, its alarm bars, never an alarm below 200 outcomes, and the P6-09 gap test numbers unchanged.</summary>
public sealed class ReliabilitySlopeTests
{
    private static readonly LoopOptions Options = new();

    /// <summary>Two prediction levels, 100 outcomes each, with the given number of wins.</summary>
    private static List<CalibrationObservation> TwoLevels(int winsAtLow, int winsAtHigh, int each = 100, double low = 0.2, double high = 0.8) =>
    [
        .. Enumerable.Range(0, each).Select(i => new CalibrationObservation("a" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), i, low, i < winsAtLow ? 1 : 0)),
        .. Enumerable.Range(0, each).Select(i => new CalibrationObservation("b" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), each + i, high, i < winsAtHigh ? 1 : 0)),
    ];

    [Fact]
    public void Observed_0_35_and_0_65_give_a_slope_of_0_4465_and_an_intercept_of_zero_and_alarm_overconfident()
    {
        var result = ReliabilitySlope.Evaluate(TwoLevels(35, 65), Options);

        result.Slope!.Value.ShouldBe(Math.Log(0.65 / 0.35) / Math.Log(0.8 / 0.2), 1e-6); // logit(0.65) / logit(0.8) = 0.619039 / 1.386294
        result.Slope!.Value.ShouldBe(0.4465, 1e-4);
        result.Intercept!.Value.ShouldBe(0, 1e-6);
        result.State.ShouldBe(ReliabilityState.Alarm);
        result.Text.ShouldContain("overconfident");
        result.Outcomes.ShouldBe(200);
    }

    [Fact]
    public void Observed_0_2_and_0_8_give_a_slope_of_one_and_no_alarm()
    {
        var result = ReliabilitySlope.Evaluate(TwoLevels(20, 80), Options);

        result.Slope!.Value.ShouldBe(1.0, 1e-6);
        result.State.ShouldBe(ReliabilityState.Ok);
    }

    [Fact]
    public void A_slope_outside_the_band_needs_to_be_more_than_the_stated_standard_errors_from_one_to_alarm()
    {
        // predictions 0.25 and 0.75, 100 each, 21 and 79 wins: slope logit(0.79) / logit(0.75) = 1.3249 / 1.0986 = 1.206, outside the 0.8 to 1.2 band, standard error about 0.158
        var outcomes = TwoLevels(21, 79, low: 0.25, high: 0.75);

        var twoSe = ReliabilitySlope.Evaluate(outcomes, Options with { SlopeSe = 2 });
        var oneSe = ReliabilitySlope.Evaluate(outcomes, Options with { SlopeSe = 1 });

        twoSe.Slope!.Value.ShouldBe(Math.Log(0.79 / 0.21) / Math.Log(0.75 / 0.25), 1e-6);
        twoSe.Slope!.Value.ShouldBe(1.206, 1e-3);
        twoSe.StandardError!.Value.ShouldBe(0.158, 5e-3);
        (twoSe.Slope!.Value - 1).ShouldBeLessThan(2 * twoSe.StandardError!.Value); // 0.206 < 0.316: outside the band but not far enough from 1
        twoSe.State.ShouldBe(ReliabilityState.Ok);
        (oneSe.Slope!.Value - 1).ShouldBeGreaterThan(oneSe.StandardError!.Value); // 0.206 > 0.158
        oneSe.State.ShouldBe(ReliabilityState.Alarm);
        oneSe.Text.ShouldContain("underconfident");
    }

    [Fact]
    public void Below_two_hundred_outcomes_the_slope_is_shown_and_never_alarmed()
    {
        var result = ReliabilitySlope.Evaluate(TwoLevels(35, 65).Skip(1).ToList(), Options);

        result.Outcomes.ShouldBe(199);
        result.State.ShouldBe(ReliabilityState.ShownOnly);
        result.Slope!.Value.ShouldBeLessThan(0.8);
        result.Text.ShouldContain("never an alarm");
    }

    [Fact]
    public void Only_the_trailing_window_is_fitted()
    {
        // 100 early outcomes that are badly calibrated, then 200 that are ideal: the early ones are outside the window
        var early = Enumerable.Range(0, 100).Select(i => new CalibrationObservation("e" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), -1000 + i, 0.2, 1));
        var result = ReliabilitySlope.Evaluate([.. early, .. TwoLevels(20, 80)], Options);

        result.Outcomes.ShouldBe(200);
        result.Slope!.Value.ShouldBe(1.0, 1e-6);
    }

    [Fact]
    public void Predictions_that_are_all_equal_or_outcomes_of_one_kind_are_unavailable()
    {
        var same = Enumerable.Range(0, 200).Select(i => new CalibrationObservation("k" + i, i, 0.5, i % 2));
        var oneKind = Enumerable.Range(0, 200).Select(i => new CalibrationObservation("k" + i, i, i % 2 == 0 ? 0.3 : 0.7, 1));

        ReliabilitySlope.Evaluate([.. same], Options).State.ShouldBe(ReliabilityState.Unavailable);
        ReliabilitySlope.Evaluate([.. oneKind], Options).State.ShouldBe(ReliabilityState.Unavailable);
        ReliabilitySlope.Evaluate([], Options).State.ShouldBe(ReliabilityState.Unavailable);
        ReliabilitySlope.Evaluate([new CalibrationObservation("x", 1, 1.5, 1)], Options).State.ShouldBe(ReliabilityState.Unavailable);
        default(ReliabilityState).ShouldBe(ReliabilityState.Unavailable);
    }

    [Fact]
    public void Probabilities_of_exactly_zero_or_one_are_held_inside_the_logit()
    {
        var edges = TwoLevels(20, 80).Select((o, i) => i % 50 == 0 ? o with { Predicted = o.Predicted < 0.5 ? 0 : 1 } : o);

        var result = ReliabilitySlope.Evaluate([.. edges], Options);

        double.IsFinite(result.Slope ?? double.NaN).ShouldBeTrue();
    }

    [Theory]
    [InlineData(100, false)] // 12 pp gap, 2.5 standard errors of 100 outcomes = 12.3 pp: not a breach
    [InlineData(200, true)] // 2.5 standard errors of 200 outcomes = 8.7 pp: a breach
    public void The_gap_test_keeps_the_p6_09_numbers(int outcomes, bool breach)
    {
        // predicted 0.58 against an observed 0.46
        var series = Enumerable.Range(0, outcomes).Select(i => new CalibrationObservation("g" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), i, 0.58, i < 46 * outcomes / 100 ? 1 : 0)).ToList();
        series.Sum(o => o.Label).ShouldBe(46 * outcomes / 100);
        var options = new DriftOptions(CalibrationWindow: outcomes, CalibrationCheckEvery: outcomes, CalibrationBreachChecks: 1);

        var events = CalibrationDriftDetector.Run("m", series, options).Value;

        events.Count.ShouldBe(breach ? 1 : 0);
        if (breach)
        {
            events[0].Value.ShouldBe(12, 1e-9);
            events[0].Limit.ShouldBeLessThan(12);
        }
    }
}
