using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The calibration is chosen by sample size (CF-10 part 10a, ADR 0214): 499 rows Platt, 500 isotonic, none the constant.</summary>
public sealed class CalibrationChoiceTests
{
    private static (double[] Scores, int[] Labels) Rows(int n) =>
        ([.. Enumerable.Range(0, n).Select(i => (i % 50) / 10.0)], [.. Enumerable.Range(0, n).Select(i => (i % 50) > 22 ? (i % 7 == 0 ? 0 : 1) : (i % 5 == 0 ? 1 : 0))]);

    [Theory]
    [InlineData(0, CalibrationKind.BaseRate)]
    [InlineData(1, CalibrationKind.Platt)]
    [InlineData(499, CalibrationKind.Platt)]
    [InlineData(500, CalibrationKind.Isotonic)]
    [InlineData(5000, CalibrationKind.Isotonic)]
    public void The_kind_follows_the_number_of_rows_against_the_cut_of_500(int rows, CalibrationKind expected)
    {
        CalibrationChoice.KindFor(rows, 500).ShouldBe(expected);
    }

    [Fact]
    public void The_cut_is_the_setting_and_must_be_at_least_one()
    {
        CalibrationChoice.KindFor(80, 80).ShouldBe(CalibrationKind.Isotonic);
        CalibrationChoice.KindFor(79, 80).ShouldBe(CalibrationKind.Platt);
        Should.Throw<ArgumentOutOfRangeException>(() => CalibrationChoice.KindFor(10, 0));
    }

    [Fact]
    public void Fitting_499_rows_gives_a_sigmoid_and_500_rows_gives_isotonic_knots()
    {
        var (s499, l499) = Rows(499);
        var (s500, l500) = Rows(500);

        var platt = CalibrationChoice.Fit(s499, l499, 500);
        var isotonic = CalibrationChoice.Fit(s500, l500, 500);

        platt.Kind.ShouldBe(CalibrationKind.Platt);
        platt.Rows.ShouldBe(499);
        platt.Platt.ShouldNotBeNull();
        platt.Scores.Count.ShouldBe(11);
        platt.Values.ShouldBe(platt.Values.Order());
        isotonic.Kind.ShouldBe(CalibrationKind.Isotonic);
        isotonic.Platt.ShouldBeNull();
        isotonic.Rows.ShouldBe(500);
    }

    [Fact]
    public void No_usable_row_gives_the_constant_one_half_and_non_finite_scores_do_not_count()
    {
        var none = CalibrationChoice.Fit([], [], 500);
        var nan = CalibrationChoice.Fit([double.NaN, double.NaN], [1, 0], 500);

        none.Kind.ShouldBe(CalibrationKind.BaseRate);
        nan.Kind.ShouldBe(CalibrationKind.BaseRate);
        none.Apply(123).ShouldBe(0.5, 1e-9);
        none.Rows.ShouldBe(0);
    }

    [Fact]
    public void A_constant_score_with_three_wins_in_eight_is_platt_0_389286_where_isotonic_gives_0_375()
    {
        double[] scores = [2, 2, 2, 2, 2, 2, 2, 2];
        int[] labels = [1, 1, 1, 0, 0, 0, 0, 0];

        var small = CalibrationChoice.Fit(scores, labels, 500);
        var big = CalibrationChoice.Fit(scores, labels, 8);

        small.Kind.ShouldBe(CalibrationKind.Platt);
        small.Scores.ShouldBe([2.0]);
        Math.Round(small.Apply(2), 6).ShouldBe(0.389286);
        big.Kind.ShouldBe(CalibrationKind.Isotonic);
        Math.Round(big.Apply(2), 6).ShouldBe(0.375);
    }

    [Fact]
    public void A_platt_map_made_from_a_stored_curve_gives_the_same_probabilities()
    {
        var (scores, labels) = Rows(120);
        var fitted = CalibrationChoice.Fit(scores, labels, 500);

        var restored = ScoreCalibration.FromPlatt(PlattCalibrator.FromParameters(fitted.Platt!.Slope, fitted.Platt.Center, fitted.Platt.Intercept), fitted.Scores, CalibrationKind.Platt, 120);

        restored.Values.ShouldBe(fitted.Values);
        restored.Apply(1.7).ShouldBe(fitted.Apply(1.7));
        Should.Throw<ArgumentException>(() => ScoreCalibration.FromPlatt(PlattCalibrator.Constant(0.5), [2, 1]));
        Should.Throw<ArgumentOutOfRangeException>(() => ScoreCalibration.FromPlatt(PlattCalibrator.Constant(0.5), [1], CalibrationKind.Isotonic));
    }

    [Theory]
    [InlineData(CalibrationKind.BaseRate, "base_rate")]
    [InlineData(CalibrationKind.Platt, "platt")]
    [InlineData(CalibrationKind.Isotonic, "isotonic")]
    public void The_stored_code_round_trips_and_an_unknown_code_is_refused(CalibrationKind kind, string code)
    {
        kind.Code().ShouldBe(code);
        CalibrationKindExtensions.FromCode(code).ShouldBe(kind);
        CalibrationKindExtensions.FromCode("sigmoid").ShouldBeNull();
        CalibrationKindExtensions.FromCode(null).ShouldBeNull();
    }
}
