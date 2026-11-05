using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>CF-07 part C, criterion 7 (ADR 0210): the plateau gate by hand. Neighbours keep 70 % of the base, the median is above 0, fewer than 3 valid points is unavailable, and the centre is reported.</summary>
public sealed class PlateauGateTests
{
    private static readonly PlateauOptions Options = new();

    private static double V(double? value) => value.ShouldNotBeNull();

    private static PlateauPoint P(double value, double? mean, int trades = 100, string? error = null) => new(value, trades, mean, error);

    private static PlateauResult Eval(double declared, double? baseMean, params (double Value, double? Mean)[] swept) =>
        PlateauGate.Evaluate("period", declared, P(declared, baseMean), [.. swept.Select(s => P(s.Value, s.Mean))], Options);

    [Fact]
    public void Base_012_with_steps_005_010_011_004_keeps_833_and_917_percent_has_median_010_and_passes()
    {
        var r = Eval(10, 0.12, (8, 0.05), (9, 0.10), (11, 0.11), (12, 0.04));

        r.Status.ShouldBe(NullStatus.Pass);
        r.Reading.ShouldBe(PlateauGate.Plateau);
        V(r.LowerRetention).ShouldBe(0.10 / 0.12, 1e-12);
        V(r.UpperRetention).ShouldBe(0.11 / 0.12, 1e-12);
        (r.LowerValue, r.UpperValue).ShouldBe((9.0, 11.0));
        V(r.Median).ShouldBe(0.10, 1e-12);
        r.ValidPoints.ShouldBe(5);
        r.Reason.ShouldContain("83.3 %");
        r.Reason.ShouldContain("91.7 %");
    }

    [Fact]
    public void The_centre_of_that_curve_is_the_declared_value_with_a_smoothed_mean_of_011()
    {
        var r = Eval(10, 0.12, (8, 0.05), (9, 0.10), (11, 0.11), (12, 0.04));

        r.CentreValue.ShouldBe(10);
        V(r.CentreMeanR).ShouldBe((0.10 + 0.12 + 0.11) / 3, 1e-12);
    }

    [Fact]
    public void Base_015_with_neighbours_002_and_002_keeps_133_percent_and_fails_as_a_spike()
    {
        var r = Eval(10, 0.15, (8, 0.01), (9, 0.02), (11, 0.02), (12, 0.01));

        r.Status.ShouldBe(NullStatus.Fail);
        r.Reading.ShouldBe(PlateauGate.Spike);
        (r.LowerRetention, r.UpperRetention).ShouldBe((0.02 / 0.15, 0.02 / 0.15));
        r.Reason.ShouldContain("13.3 %");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.05)]
    public void A_base_that_is_not_above_zero_is_no_base_edge_and_a_fail(double baseMean)
    {
        var r = Eval(10, baseMean, (8, 0.05), (9, 0.10), (11, 0.11), (12, 0.04));

        r.Reading.ShouldBe(PlateauGate.NoBaseEdge);
        r.Status.ShouldBe(NullStatus.Fail);
        r.LowerRetention.ShouldBeNull("a ratio to a base that is not positive means nothing");
    }

    [Fact]
    public void The_research_example_of_opening_range_lengths_passes_at_15_with_the_centre_at_15()
    {
        var r = Eval(15, 0.12, (5, 0.02), (10, 0.11), (20, 0.10), (30, 0.01));

        r.Status.ShouldBe(NullStatus.Pass);
        V(r.LowerRetention).ShouldBe(0.11 / 0.12, 1e-12);
        V(r.UpperRetention).ShouldBe(0.10 / 0.12, 1e-12);
        V(r.Median).ShouldBe(0.10, 1e-12);
        r.CentreValue.ShouldBe(15);
        V(r.CentreMeanR).ShouldBe(0.11, 1e-12);
    }

    [Fact]
    public void The_research_example_with_a_spike_at_15_fails_and_the_neighbours_keep_133_percent()
    {
        var r = Eval(15, 0.15, (5, 0.01), (10, 0.02), (20, 0.02), (30, 0.01));

        r.Status.ShouldBe(NullStatus.Fail);
        V(r.LowerRetention).ShouldBe(0.02 / 0.15, 1e-12);
    }

    [Fact]
    public void A_centre_away_from_the_declared_value_is_reported_and_the_declared_value_is_not_changed()
    {
        // declared 10 at 0.12 with a better shoulder at 12: smoothed at 11 = (0.12 + 0.20 + 0.19) / 3 = 0.17, above 0.1067 at 10
        var r = Eval(10, 0.12, (8, 0.04), (9, 0.10), (11, 0.20), (12, 0.19));

        r.Declared.ShouldBe(10);
        r.CentreValue.ShouldBe(11);
        V(r.CentreMeanR).ShouldBe((0.12 + 0.20 + 0.19) / 3, 1e-12);
    }

    [Fact]
    public void Neighbours_that_keep_exactly_seventy_percent_pass_and_a_hair_under_fails()
    {
        // 0.14 / 0.20 is 0.70 in exact arithmetic
        Eval(10, 0.20, (9, 0.14), (11, 0.15)).Status.ShouldBe(NullStatus.Pass);
        Eval(10, 0.20, (9, 0.139), (11, 0.15)).Status.ShouldBe(NullStatus.Fail);
        Eval(10, 0.20, (9, 0.15), (11, 0.139)).Status.ShouldBe(NullStatus.Fail);
    }

    [Fact]
    public void A_negative_neighbour_fails_whatever_the_median_is()
    {
        var r = Eval(10, 0.10, (8, 0.09), (9, -0.01), (11, 0.09), (12, 0.09));

        r.Status.ShouldBe(NullStatus.Fail);
        r.Reading.ShouldBe(PlateauGate.Spike);
    }

    [Fact]
    public void A_median_that_is_not_above_zero_fails_even_when_the_neighbours_keep_their_share()
    {
        // seven points: the declared 0.10, neighbours 0.08 and 0.08, four far points at -0.10: the median is -0.10
        var r = PlateauGate.Evaluate("period", 10, P(10, 0.10), [P(8, -0.10), P(9, 0.08), P(11, 0.08), P(12, -0.10), P(6, -0.10), P(14, -0.10)], Options);

        r.Status.ShouldBe(NullStatus.Fail);
        V(r.Median).ShouldBe(-0.10, 1e-12);
        V(r.LowerRetention).ShouldBe(0.8, 1e-12);
        r.Reason.ShouldContain("median");
    }

    [Fact]
    public void The_median_of_an_even_number_of_valid_points_is_the_mean_of_the_middle_two()
    {
        var r = PlateauGate.Evaluate("period", 10, P(10, 0.10), [P(8, 0.02), P(9, 0.09), P(11, 0.08)], Options);

        V(r.Median).ShouldBe((0.08 + 0.09) / 2, 1e-12);
    }

    [Fact]
    public void Fewer_than_three_valid_points_is_unavailable_and_never_a_pass()
    {
        var onlyOne = Eval(10, 0.12, (9, 0.11));
        var oneRefused = PlateauGate.Evaluate("period", 10, P(10, 0.12), [P(9, 0.11), new PlateauPoint(11, 0, null, "refused")], Options);
        var none = PlateauGate.Evaluate("period", 10, P(10, 0.12), [], Options);

        foreach (var r in new[] { onlyOne, oneRefused, none })
        {
            r.Reading.ShouldBe(PlateauGate.Inconclusive);
            r.Status.ShouldBe(NullStatus.Unavailable);
            r.Reason.ShouldContain("3 are needed");
        }

        onlyOne.ValidPoints.ShouldBe(2);
    }

    [Fact]
    public void Three_valid_points_on_one_side_of_the_declared_value_have_no_neighbour_on_the_other_and_are_unavailable()
    {
        var r = Eval(10, 0.12, (11, 0.11), (12, 0.10));

        r.Status.ShouldBe(NullStatus.Unavailable);
        r.Reason.ShouldContain("lower side");
        Eval(10, 0.12, (8, 0.11), (9, 0.10)).Reason.ShouldContain("upper side");
    }

    [Fact]
    public void A_point_next_to_the_declared_value_that_ran_no_trade_is_unavailable_not_skipped()
    {
        var r = PlateauGate.Evaluate("period", 10, P(10, 0.12), [P(8, 0.11), P(9, null, 0), P(11, 0.11), P(12, 0.10)], Options);

        r.Status.ShouldBe(NullStatus.Unavailable);
        r.Reason.ShouldContain("9");
        r.Reason.ShouldContain("no trades");
    }

    [Fact]
    public void A_refused_neighbour_names_the_refusal()
    {
        var r = PlateauGate.Evaluate("period", 10, P(10, 0.12), [P(8, 0.11), new PlateauPoint(9, 0, null, "refused: window"), P(11, 0.11), P(12, 0.10)], Options);

        r.Status.ShouldBe(NullStatus.Unavailable);
        r.Reason.ShouldContain("refused: window");
    }

    [Fact]
    public void A_base_run_with_no_result_is_unavailable()
    {
        var r = PlateauGate.Evaluate("period", 10, new PlateauPoint(10, 0, null, "no data"), [P(8, 0.1), P(9, 0.1), P(11, 0.1)], Options);

        r.Status.ShouldBe(NullStatus.Unavailable);
        r.Reason.ShouldContain("no data");
        r.BaseMeanR.ShouldBeNull();
    }

    [Fact]
    public void A_larger_minimum_makes_a_thin_curve_unavailable()
    {
        var r = PlateauGate.Evaluate("period", 10, P(10, 0.12), [P(9, 0.11), P(11, 0.11)], new PlateauOptions(MinPoints: 4));

        r.Status.ShouldBe(NullStatus.Unavailable);
    }

    [Fact]
    public void A_stricter_retention_fails_a_curve_that_a_looser_one_passes()
    {
        PlateauGate.Evaluate("period", 10, P(10, 0.12), [P(9, 0.10), P(11, 0.11)], new PlateauOptions(Retention: 0.90)).Status.ShouldBe(NullStatus.Fail);
        PlateauGate.Evaluate("period", 10, P(10, 0.12), [P(9, 0.10), P(11, 0.11)], new PlateauOptions(Retention: 0.80)).Status.ShouldBe(NullStatus.Pass);
    }

    [Fact]
    public void The_order_the_swept_points_are_given_in_changes_nothing()
    {
        var a = Eval(10, 0.12, (8, 0.05), (9, 0.10), (11, 0.11), (12, 0.04));
        var b = Eval(10, 0.12, (12, 0.04), (11, 0.11), (8, 0.05), (9, 0.10));

        b.ShouldBeEquivalentTo(a);
        a.Points.Select(p => p.Value).ShouldBe([8.0, 9.0, 10.0, 11.0, 12.0]);
    }

    [Fact]
    public void Only_a_valid_point_with_two_valid_neighbours_can_be_the_centre()
    {
        var r = PlateauGate.Evaluate("period", 10, P(10, 0.12), [P(9, 0.11), P(11, null, 0)], Options);

        r.CentreValue.ShouldBeNull();
        r.CentreMeanR.ShouldBeNull();
    }

    [Fact]
    public void A_tie_in_the_smoothed_mean_goes_to_the_lower_value()
    {
        var r = PlateauGate.Evaluate("period", 10, P(10, 0.10), [P(8, 0.10), P(9, 0.10), P(11, 0.10), P(12, 0.10)], Options);

        r.CentreValue.ShouldBe(9);
    }

    [Fact]
    public void The_gate_passes_only_when_every_parameter_passes_and_a_fail_beats_an_unavailable()
    {
        var pass = Eval(10, 0.12, (9, 0.11), (11, 0.11));
        var fail = Eval(10, 0.15, (9, 0.02), (11, 0.02));
        var open = Eval(10, 0.12, (9, 0.11));

        PlateauGate.Overall([pass, pass]).Status.ShouldBe(NullStatus.Pass);
        PlateauGate.Overall([pass, fail, open]).Status.ShouldBe(NullStatus.Fail);
        PlateauGate.Overall([pass, open]).Status.ShouldBe(NullStatus.Unavailable);
        PlateauGate.Overall([pass, fail]).Reason.ShouldContain("period");
    }

    [Fact]
    public void A_strategy_with_no_swept_parameter_reads_unavailable_never_a_pass()
    {
        var (status, reason) = PlateauGate.Overall([]);

        status.ShouldBe(NullStatus.Unavailable);
        reason.ShouldContain("no parameter");
    }
}
