using XDrishti.Domain.Pairs;

namespace XDrishti.Domain.Tests.Pairs;

/// <summary>Spec P14-12 criterion 1: every hand-worked value (recomputed independently in Python before it was pinned), the BH cases and the signal transitions.</summary>
public sealed class PairsHandTableTests
{
    private static readonly double[] X = [1, 2, 3, 4, 5];
    private static readonly double[] Y = [3.05, 4.90, 7.00, 9.10, 10.95];

    /// <summary>The algebra check of the spec: 4 equations, no lags; the minimum-length rule is lowered through the options.</summary>
    private static readonly PairsOptions Tiny = new() { Lags = 0, MinObservations = 5 };

    [Fact]
    public void Ols_of_the_hand_table_recovers_alpha_beta_and_the_residuals()
    {
        var fit = HedgeRatio.Ols(Y, X, Tiny).Value;

        fit.Beta.ShouldBe(2.0, 1e-6);
        fit.Alpha.ShouldBe(1.0, 1e-6);
        fit.N.ShouldBe(5);
        double[] e = [0.05, -0.10, 0.0, 0.10, -0.05];
        for (var i = 0; i < 5; i++)
        {
            fit.Residuals[i].ShouldBe(e[i], 1e-6);
        }
    }

    [Fact]
    public void The_engle_granger_statistic_of_the_hand_table_is_minus_2_792848()
    {
        var result = EngleGranger.Test(Y, X, Tiny).Value;

        result.Gamma.ShouldBe(-1.444444, 1e-6);
        result.Tau.ShouldBe(-2.792848, 1e-6);
        result.Equations.ShouldBe(4);
        result.LagsUsed.ShouldBe(0);
        result.N.ShouldBe(5);
        result.RejectsAt5Percent.ShouldBeFalse();
        result.Ssr.ShouldBe(0.018056, 1e-6);
        (result.Ssr / 3).ShouldBe(0.006019, 1e-6);
        result.SeGamma.ShouldBe(0.517194, 1e-6);
    }

    [Fact]
    public void The_critical_values_of_a_test_are_taken_at_n_minus_one_observations()
    {
        var rng = new XDrishti.Domain.Lab.SeededRandom(17);
        var y = PairsData.Walk(rng, 63);
        var x = PairsData.Walk(rng, 63);

        var c = EngleGranger.Test(y, x).Value.Critical;

        c.SampleSize.ShouldBe(62);
        c.Percent1.ShouldBe(-4.07894, 1e-4);
        c.Percent5.ShouldBe(-3.43646, 1e-4);
    }

    [Fact]
    public void The_hand_table_regression_sums_match_the_published_intermediates()
    {
        // SSR 0.018056, s^2 0.006019, se(gamma) 0.517194 follow from tau = gamma / se: se = gamma / tau.
        var result = EngleGranger.Test(Y, X, Tiny).Value;

        (result.Gamma / result.Tau).ShouldBe(0.517194, 1e-6);
    }

    [Theory]
    [InlineData(252, -3.9403, -3.3605, -3.0613)]
    [InlineData(63, -4.0760, -3.4348, -3.1125)]
    public void The_finite_sample_critical_values_match_the_table(int t, double one, double five, double ten)
    {
        var c = MacKinnonTables.Critical(t);

        c.Percent1.ShouldBe(one, 1e-4);
        c.Percent5.ShouldBe(five, 1e-4);
        c.Percent10.ShouldBe(ten, 1e-4);
        c.SampleSize.ShouldBe(t);
    }

    [Fact]
    public void The_one_percent_second_coefficient_is_the_papers_minus_22_527_not_the_libraries_minus_33_527()
    {
        // ADR 0192, plan error 1: Table 2 of MacKinnon (2010) prints -22.527 for N = 2 at 1 percent; statsmodels and arch print -33.527.
        MacKinnonTables.Critical(63).Percent1.ShouldBe(-3.89644 - (10.9519 / 63) - (22.527 / 3969), 1e-12);
    }

    [Fact]
    public void Half_life_of_minus_0_10_is_6_578813_bars()
    {
        var s = Decaying(0.9);

        HalfLife.Of(s).Value.ShouldBe(6.578813, 1e-6);
    }

    [Fact]
    public void Half_life_of_a_flat_slope_is_not_mean_reverting_and_of_minus_1_2_oscillating()
    {
        var ramp = Enumerable.Range(0, 80).Select(i => 5.0 + (0.01 * i)).ToArray();

        HalfLife.Of(ramp).Reason.ShouldBe(PairsReason.NotMeanReverting);
        HalfLife.Of(Decaying(-0.2)).Reason.ShouldBe(PairsReason.Oscillating);
    }

    [Fact]
    public void Benjamini_hochberg_rejects_the_first_four_of_the_spec_example()
    {
        var outcome = BenjaminiHochberg.Reject([0.001, 0.008, 0.02, 0.04, 0.30], 0.05).Value;

        outcome.K.ShouldBe(4);
        outcome.M.ShouldBe(5);
        outcome.Rejected.ShouldBe([true, true, true, true, false]);
    }

    [Fact]
    public void An_unavailable_test_counts_in_m_with_p_one_and_the_fourth_is_lost()
    {
        var outcome = BenjaminiHochberg.Reject([0.001, 0.008, 0.02, 0.04, 0.30, 1.0], 0.05).Value;

        outcome.K.ShouldBe(3);
        outcome.M.ShouldBe(6);
        outcome.Rejected.ShouldBe([true, true, true, false, false, false]);
    }

    [Fact]
    public void An_unavailable_engle_granger_result_enters_bh_with_p_one()
    {
        var good = EngleGranger.Test(Y, X, Tiny);
        var none = new PairsResult<EngleGrangerResult>();

        BenjaminiHochberg.Reject([good, none, none], 0.05).Value.M.ShouldBe(3);
        BenjaminiHochberg.Reject([good, none, none], 0.05).Value.Rejected[1].ShouldBeFalse();
    }

    [Fact]
    public void The_hand_z_score_is_3_794733()
    {
        var options = new PairsOptions { MinWindow = 5 };

        ZScore.Of([0.010, 0.012, 0.008, 0.011, 0.009], 0.016, options).Value.ShouldBe(3.794733, 1e-6);
    }

    [Fact]
    public void That_z_opens_a_shortZspread_from_flat_and_stops_a_long_spread()
    {
        var z = ZScore.Of([0.010, 0.012, 0.008, 0.011, 0.009], 0.016, new PairsOptions { MinWindow = 5 });

        PairSignal.Next(PairPosition.Flat, z).Action.ShouldBe(SignalAction.EnterShortSpread);
        PairSignal.Next(PairPosition.LongSpread, z).Action.ShouldBe(SignalAction.Stop);
    }

    [Theory]
    [InlineData(PairPosition.Flat, -2.5, SignalAction.EnterLongSpread, PairPosition.LongSpread)]
    [InlineData(PairPosition.Flat, -2.0, SignalAction.EnterLongSpread, PairPosition.LongSpread)]
    [InlineData(PairPosition.Flat, -1.9, SignalAction.Hold, PairPosition.Flat)]
    [InlineData(PairPosition.Flat, 0.0, SignalAction.Hold, PairPosition.Flat)]
    [InlineData(PairPosition.Flat, 1.9, SignalAction.Hold, PairPosition.Flat)]
    [InlineData(PairPosition.Flat, 2.0, SignalAction.EnterShortSpread, PairPosition.ShortSpread)]
    [InlineData(PairPosition.Flat, 3.0, SignalAction.EnterShortSpread, PairPosition.ShortSpread)]
    [InlineData(PairPosition.LongSpread, -2.5, SignalAction.Hold, PairPosition.LongSpread)]
    [InlineData(PairPosition.LongSpread, -0.6, SignalAction.Hold, PairPosition.LongSpread)]
    [InlineData(PairPosition.LongSpread, -0.5, SignalAction.Exit, PairPosition.Flat)]
    [InlineData(PairPosition.LongSpread, 0.2, SignalAction.Exit, PairPosition.Flat)]
    [InlineData(PairPosition.LongSpread, 0.5, SignalAction.Exit, PairPosition.Flat)]
    [InlineData(PairPosition.LongSpread, 3.4, SignalAction.Hold, PairPosition.LongSpread)]
    [InlineData(PairPosition.LongSpread, 3.5, SignalAction.Stop, PairPosition.Flat)]
    [InlineData(PairPosition.LongSpread, -3.5, SignalAction.Stop, PairPosition.Flat)]
    [InlineData(PairPosition.ShortSpread, 2.5, SignalAction.Hold, PairPosition.ShortSpread)]
    [InlineData(PairPosition.ShortSpread, 0.4, SignalAction.Exit, PairPosition.Flat)]
    [InlineData(PairPosition.ShortSpread, 3.6, SignalAction.Stop, PairPosition.Flat)]
    [InlineData(PairPosition.ShortSpread, -3.6, SignalAction.Stop, PairPosition.Flat)]
    public void The_transitions_table(PairPosition from, double zValue, SignalAction action, PairPosition to)
    {
        var step = PairSignal.Next(from, ZOf(zValue));

        step.Action.ShouldBe(action);
        step.Position.ShouldBe(to);
        step.Reason.ShouldBeNull();
    }

    [Theory]
    [InlineData(PairPosition.Flat)]
    [InlineData(PairPosition.LongSpread)]
    [InlineData(PairPosition.ShortSpread)]
    public void An_unavailable_z_holds_the_position_and_is_counted(PairPosition from)
    {
        var step = PairSignal.Next(from, new PairsResult<double>());
        var shortZ = PairSignal.Next(from, ZScore.Of([1.0, 2.0], 1.5));

        step.Action.ShouldBe(SignalAction.Hold);
        step.Position.ShouldBe(from);
        step.Reason.ShouldBe(PairsReason.ZUnavailable);
        shortZ.Action.ShouldBe(SignalAction.Hold);
        shortZ.Reason.ShouldBe(PairsReason.ZUnavailable);
    }

    [Fact]
    public void Return_correlation_of_a_series_with_itself_is_one_and_with_its_mirror_minus_one()
    {
        var rng = new XDrishti.Domain.Lab.SeededRandom(7);
        var a = PairsData.Walk(rng, 100);
        var b = a.Select(v => -2.0 * v).ToArray();

        ReturnCorrelation.Of(a, a).Value.ShouldBe(1.0, 1e-12);
        ReturnCorrelation.Of(a, b).Value.ShouldBe(-1.0, 1e-12);
    }

    [Fact]
    public void The_log_spread_applies_a_fixed_hedge_ratio()
    {
        LogSpread.Of([3.0, 4.0], [1.0, 2.0], 1.0, 1.5).Value.ShouldBe([0.5, 0.0]);
        LogSpread.Ln([1.0, Math.E]).Value.ShouldBe([0.0, 1.0]);
    }

    [Fact]
    public void The_half_life_range_is_the_pre_registered_one_to_twenty_sessions()
    {
        HalfLife.InRange(1).ShouldBeTrue();
        HalfLife.InRange(20).ShouldBeTrue();
        HalfLife.InRange(0.5).ShouldBeFalse();
        HalfLife.InRange(20.1).ShouldBeFalse();
        HalfLife.InRange(double.NaN).ShouldBeFalse();
    }

    private static PairsResult<double> ZOf(double z) => ZScore.Of([-1.0, 1.0, -1.0, 1.0, -1.0, 1.0, -1.0, 1.0, -1.0, 1.0, -1.0, 1.0, -1.0, 1.0, -1.0, 1.0, -1.0, 1.0, -1.0, 1.0, -1.0, 1.0, -1.0, 1.0, -1.0, 1.0, -1.0, 1.0, -1.0, 1.0, 0.0], z * Sd, new PairsOptions { MinWindow = 31 });

    // The window above has 31 values: mean = (-15 + 15) / 31 = 0 and sample variance 30 / 30 = 1, so sd = 1 and z is the value itself.
    private const double Sd = 1.0;

    private static double[] Decaying(double factor)
    {
        var s = new double[80];
        s[0] = 10.0;
        for (var i = 1; i < s.Length; i++)
        {
            s[i] = factor * s[i - 1];
        }

        return s;
    }
}
