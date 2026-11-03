using XDrishti.Domain.Lab;
using XDrishti.Domain.Pairs;

namespace XDrishti.Domain.Tests.Pairs;

/// <summary>Spec P14-12 criterion 6: each closed reason is reached by a case, no result carries both a value and a reason, nothing throws on a number.</summary>
public sealed class PairsHonestStatesTests
{
    private static (double[] Y, double[] X) Pair(int n = 100)
    {
        var rng = new SeededRandom(21);
        var x = PairsData.Walk(rng, n);
        var u = PairsData.Ar1(rng, n, 0.5, 0.01);
        return (x.Select((v, i) => 1 + (0.8 * v) + u[i]).ToArray(), x);
    }

    [Fact]
    public void Bad_input_is_a_non_finite_number_or_unequal_lengths_or_a_non_positive_price()
    {
        var (y, x) = Pair();
        var withNaN = (double[])y.Clone();
        withNaN[10] = double.NaN;
        var withInf = (double[])x.Clone();
        withInf[3] = double.PositiveInfinity;

        EngleGranger.Test(withNaN, x).Reason.ShouldBe(PairsReason.BadInput);
        HedgeRatio.Ols(y, withInf).Reason.ShouldBe(PairsReason.BadInput);
        HedgeRatio.Ols(y, x.AsSpan(0, 50)).Reason.ShouldBe(PairsReason.TooShort);
        LogSpread.Ln([1.0, 0.0]).Reason.ShouldBe(PairsReason.BadInput);
        LogSpread.Ln([1.0, -2.0]).Reason.ShouldBe(PairsReason.BadInput);
        LogSpread.Ln([1.0, double.NaN]).Reason.ShouldBe(PairsReason.BadInput);
        EngleGranger.TestPrices([1.0, 2.0], [0.0, 1.0]).Reason.ShouldBe(PairsReason.BadInput);
        HalfLife.Of(withNaN).Reason.ShouldBe(PairsReason.BadInput);
        ZScore.Of(new double[40], double.NaN).Reason.ShouldBe(PairsReason.BadInput);
        ReturnCorrelation.Of(y, x.AsSpan(0, 50)).Reason.ShouldBe(PairsReason.TooShort);
        BenjaminiHochberg.Reject([0.1], 0.0).Reason.ShouldBe(PairsReason.BadInput);
        EngleGranger.Test(y, x, new PairsOptions { Lags = -1 }).Reason.ShouldBe(PairsReason.BadInput);
        PairSignal.Next(PairPosition.Flat, ZScore.Of(new double[40], 1), new PairsOptions { Entry = 1, Exit = 2 }).Reason.ShouldBe(PairsReason.BadInput);
    }

    [Fact]
    public void An_undefined_position_comes_back_unchanged_with_bad_input()
    {
        var step = PairSignal.Next((PairPosition)7, ZScore.Of(new double[40], 1.0));

        step.Position.ShouldBe((PairPosition)7);
        step.Action.ShouldBe(SignalAction.Hold);
        step.Reason.ShouldBe(PairsReason.BadInput);
    }

    [Fact]
    public void Too_short_is_fewer_than_the_minimum_observations()
    {
        var (y, x) = Pair(59);

        EngleGranger.Test(y, x).Reason.ShouldBe(PairsReason.TooShort);
        HedgeRatio.Ols(y, x).Reason.ShouldBe(PairsReason.TooShort);
        HalfLife.Of(y).Reason.ShouldBe(PairsReason.TooShort);
        ReturnCorrelation.Of(y, x).Reason.ShouldBe(PairsReason.TooShort);
        EngleGranger.Test(Pair(100).Y, Pair(100).X, new PairsOptions { Lags = 20, MinObservations = 10 }).IsAvailable.ShouldBeTrue();
        EngleGranger.Test(Pair(30).Y, Pair(30).X, new PairsOptions { Lags = 20, MinObservations = 10 }).Reason.ShouldBe(PairsReason.TooShort);
    }

    [Fact]
    public void Degenerate_x_is_a_constant_regressor()
    {
        var (y, _) = Pair();
        var flat = new double[y.Length];
        Array.Fill(flat, 4.2);

        HedgeRatio.Ols(y, flat).Reason.ShouldBe(PairsReason.DegenerateX);
        EngleGranger.Test(y, flat).Reason.ShouldBe(PairsReason.DegenerateX);
        HalfLife.Of(flat).Reason.ShouldBe(PairsReason.DegenerateX);
        ReturnCorrelation.Of(y, flat).Reason.ShouldBe(PairsReason.DegenerateX);
    }

    [Fact]
    public void Zero_residual_variance_is_an_exact_relation_or_a_constant_y()
    {
        var (_, x) = Pair();
        var exact = x.Select(v => 2.0 + (3.0 * v)).ToArray();
        var constant = new double[x.Length];
        Array.Fill(constant, 1.5);

        EngleGranger.Test(exact, x).Reason.ShouldBe(PairsReason.ZeroResidualVariance);
        EngleGranger.Test(constant, x).Reason.ShouldBe(PairsReason.ZeroResidualVariance);
    }

    [Fact]
    public void Not_mean_reverting_and_oscillating_come_from_the_half_life()
    {
        var explosive = Enumerable.Range(0, 80).Select(i => Math.Pow(1.05, i)).ToArray();
        var flipping = Enumerable.Range(0, 80).Select(i => Math.Pow(-0.2, i) * 10).ToArray();

        HalfLife.Of(explosive).Reason.ShouldBe(PairsReason.NotMeanReverting);
        HalfLife.Of(flipping).Reason.ShouldBe(PairsReason.Oscillating);
    }

    [Fact]
    public void Short_window_and_flat_window_come_from_the_z_score()
    {
        ZScore.Of(new double[29], 1.0).Reason.ShouldBe(PairsReason.ShortWindow);
        ZScore.Of(Enumerable.Repeat(0.01, 40).ToArray(), 0.02).Reason.ShouldBe(PairsReason.FlatWindow);
    }

    [Fact]
    public void Z_unavailable_is_the_reason_of_a_held_step()
    {
        PairSignal.Next(PairPosition.ShortSpread, ZScore.Of(new double[29], 1.0)).Reason.ShouldBe(PairsReason.ZUnavailable);
    }

    [Fact]
    public void A_default_result_is_unavailable_and_reading_its_value_throws_only_as_a_caller_bug()
    {
        var none = new PairsResult<double>();

        none.IsAvailable.ShouldBeFalse();
        none.Reason.ShouldBe(PairsReason.BadInput);
        none.TryGet(out _).ShouldBeFalse();
        Should.Throw<InvalidOperationException>(() => none.Value);
    }

    [Fact]
    public void No_result_carries_both_a_value_and_a_reason_over_a_sweep_of_odd_inputs()
    {
        var rng = new SeededRandom(5);
        double[] odd = [0, 1e-300, -1e300, 1e300, double.NaN, double.PositiveInfinity, double.Epsilon, 1, -1];
        for (var round = 0; round < 300; round++)
        {
            var n = rng.Next(130);
            var y = Enumerable.Range(0, n).Select(_ => rng.Next(7) == 0 ? odd[rng.Next(odd.Length)] : PairsData.Normal(rng)).ToArray();
            var x = Enumerable.Range(0, n).Select(_ => rng.Next(9) == 0 ? odd[rng.Next(odd.Length)] : PairsData.Normal(rng)).ToArray();

            Both(EngleGranger.Test(y, x));
            Both(HedgeRatio.Ols(y, x));
            Both(HalfLife.Of(y));
            Both(ReturnCorrelation.Of(y, x));
            Both(ZScore.Of(y, x.Length > 0 ? x[0] : 0));
            Both(EngleGranger.TestPrices(y, x));
            Both(BenjaminiHochberg.Reject(y, 0.05));
        }
    }

    private static void Both<T>(PairsResult<T> r)
        where T : notnull
    {
        (r.IsAvailable == r.Reason.HasValue).ShouldBeFalse();
        if (r.IsAvailable && r.Value is double d)
        {
            double.IsFinite(d).ShouldBeTrue();
        }

        if (r.IsAvailable && r.Value is EngleGrangerResult e)
        {
            double.IsFinite(e.Tau).ShouldBeTrue();
            e.PValue.ShouldBeInRange(0.0, 1.0);
        }
    }
}
