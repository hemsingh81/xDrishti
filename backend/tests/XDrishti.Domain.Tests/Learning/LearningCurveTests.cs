using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-12 part 12a, AC9 (ADR 0218, spec D5): the slope over the last 3 to 5 points, rising, flat or unclear, and the projection.</summary>
public sealed class LearningCurveTests
{
    private static readonly LoopOptions Options = new();

    private static List<LearningCurvePoint> Points(params double[] gains) => [.. gains.Select((g, i) => new LearningCurvePoint(50 * (i + 1), g))];

    [Fact]
    public void Three_points_by_hand_give_a_slope_of_0_00009_per_row_and_a_projection_of_about_54_more_rows()
    {
        var result = LearningCurve.Evaluate(Points(-0.004, 0.001, 0.005), Options);

        result.SlopePerRow!.Value.ShouldBe(0.00009, 1e-12);
        result.SlopePer50Rows!.Value.ShouldBe(0.0045, 1e-12);
        result.ProjectedRows!.Value.ShouldBe(203.7, 0.05); // 100 + 0.009333 / 0.00009
        result.MoreRows!.Value.ShouldBe(53.7, 0.05);
        result.Verdict.ShouldBe(CurveVerdict.Unclear); // three points: no standard error
        result.StandardError.ShouldBeNull();
        result.Text.ShouldContain("not a finding");
    }

    [Fact]
    public void A_flat_series_with_a_tight_error_is_flat()
    {
        var result = LearningCurve.Evaluate(Points(0.004, 0.0041, 0.0039, 0.0040), Options);

        result.Verdict.ShouldBe(CurveVerdict.Flat);
        result.StandardError!.Value.ShouldBe(8.5e-7, 1e-7);
        result.Text.ShouldContain("more data will not help this recipe");
        result.ProjectedRows.ShouldBeNull();
    }

    [Fact]
    public void A_steady_climb_with_a_small_error_is_rising_and_is_projected_as_a_finding()
    {
        var result = LearningCurve.Evaluate(Points(0.000, 0.0021, 0.0039, 0.0060, 0.0081), Options);

        result.Verdict.ShouldBe(CurveVerdict.Rising);
        result.PointsUsed.ShouldBe(5);
        result.ProjectedRows.ShouldNotBeNull();
        result.Text.ShouldNotContain("not a finding");
    }

    [Fact]
    public void A_noisy_series_is_unclear()
    {
        LearningCurve.Evaluate(Points(0.002, -0.004, 0.006, -0.002, 0.001), Options).Verdict.ShouldBe(CurveVerdict.Unclear);
    }

    [Fact]
    public void Only_the_last_five_points_are_fitted()
    {
        // eight points: a fall then a steady climb in the last five
        var result = LearningCurve.Evaluate(Points(0.05, 0.03, 0.01, 0.000, 0.0021, 0.0039, 0.0060, 0.0081), Options);

        result.PointsUsed.ShouldBe(5);
        result.Verdict.ShouldBe(CurveVerdict.Rising);
    }

    [Fact]
    public void A_line_already_at_the_target_needs_no_more_rows()
    {
        var result = LearningCurve.Evaluate(Points(0.011, 0.012, 0.013), Options);

        result.MoreRows.ShouldBe(0);
        result.ProjectedRows.ShouldBe(150);
    }

    [Fact]
    public void Fewer_than_three_points_or_unreadable_points_are_insufficient()
    {
        LearningCurve.Evaluate(Points(0.1, 0.2), Options).Verdict.ShouldBe(CurveVerdict.Insufficient);
        LearningCurve.Evaluate([], Options).Verdict.ShouldBe(CurveVerdict.Insufficient);
        LearningCurve.Evaluate([new LearningCurvePoint(50, 0.1), new LearningCurvePoint(50, 0.2), new LearningCurvePoint(100, 0.3)], Options).Verdict.ShouldBe(CurveVerdict.Insufficient);
        LearningCurve.Evaluate(Points(0.1, double.NaN, 0.3), Options).Verdict.ShouldBe(CurveVerdict.Insufficient);
        default(CurveVerdict).ShouldBe(CurveVerdict.Insufficient);
    }

    [Fact]
    public void Points_may_arrive_in_any_order()
    {
        var ordered = Points(-0.004, 0.001, 0.005);

        LearningCurve.Evaluate([.. ordered.AsEnumerable().Reverse()], Options).ShouldBe(LearningCurve.Evaluate(ordered, Options));
    }
}
