using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The calibration error with a day-cluster bootstrap interval (CF-10 part 10a, ADR 0214).</summary>
public sealed class EceIntervalTests
{
    private static readonly DateOnly Start = new(2025, 1, 6);

    private static (double[] Predicted, int[] Labels, DateOnly[] Days) Constant(int n, double p, int wins, int perDay = 1) =>
        ([.. Enumerable.Repeat(p, n)], [.. Enumerable.Range(0, n).Select(i => i < wins ? 1 : 0)], [.. Enumerable.Range(0, n).Select(i => Start.AddDays(i / perDay))]);

    private static CalibrationAssessment Assess((double[] P, int[] L, DateOnly[] D) d, string seed = "m1", int draws = 1000) =>
        EceInterval.Assess(d.P, d.L, d.D, draws, 0.90, 10, seed).Value;

    [Fact]
    public void A_hundred_outcomes_predicted_55_percent_observed_50_percent_are_a_gap_of_5_pp_and_one_standard_error()
    {
        var a = Assess(Constant(100, 0.55, 50));

        a.N.ShouldBe(100);
        Math.Round(a.GapPp, 6).ShouldBe(5);
        Math.Round(a.StandardErrorPp, 6).ShouldBe(5);   // sqrt(0.5 x 0.5 / 100)
        Math.Round(a.EcePp, 6).ShouldBe(5);
    }

    [Fact]
    public void The_interval_is_deterministic_for_a_seed_and_moves_with_another_seed()
    {
        var d = Constant(100, 0.55, 50);

        var first = Assess(d);
        var again = Assess(d);
        var other = Assess(d, "m2");

        again.ShouldBe(first);
        (other.EceLowPp, other.EceHighPp).ShouldNotBe((first.EceLowPp, first.EceHighPp));
        first.EceLowPp.ShouldBeLessThanOrEqualTo(first.EceHighPp);
        first.EceLowPp.ShouldBeGreaterThanOrEqualTo(0);
        first.EceHighPp.ShouldBeInRange(10, 16);   // the resampled win count is about 50 +- 1.645 x 5, so the error runs up to about 5 + 8
        first.Draws.ShouldBe(1000);
        first.Level.ShouldBe(0.90);
    }

    [Fact]
    public void All_trades_on_one_day_cannot_be_told_apart_by_resampling_days()
    {
        var a = Assess(Constant(100, 0.55, 50, perDay: 100));

        a.Days.ShouldBe(1);
        a.EceLowPp.ShouldBe(a.EcePp, 1e-9);
        a.EceHighPp.ShouldBe(a.EcePp, 1e-9);
    }

    [Fact]
    public void Clustering_by_day_gives_a_wider_interval_than_one_trade_a_day()
    {
        var perTrade = Assess(Constant(100, 0.55, 50, perDay: 1));
        var clustered = Assess(Constant(100, 0.55, 50, perDay: 10));

        (clustered.EceHighPp - clustered.EceLowPp).ShouldBeGreaterThan(perTrade.EceHighPp - perTrade.EceLowPp);
    }

    [Fact]
    public void The_log_loss_is_set_against_the_constant_that_is_the_observed_rate()
    {
        var a = Assess(Constant(100, 0.55, 50));

        Math.Round(a.BaseRateLogLoss, 6).ShouldBe(Math.Round(Math.Log(2), 6));
        Math.Round(a.LogLoss, 6).ShouldBe(Math.Round(-((0.5 * Math.Log(0.55)) + (0.5 * Math.Log(0.45))), 6));
        a.LogLossMinusBaseRate.ShouldBeGreaterThan(0);   // 0.55 is not the best constant, so it loses to the observed rate
    }

    [Fact]
    public void A_perfectly_one_sided_set_has_no_standard_error_and_no_base_rate_loss()
    {
        var a = Assess(Constant(60, 0.9, 60));

        a.StandardErrorPp.ShouldBe(0);
        a.BaseRateLogLoss.ShouldBe(0);
    }

    [Fact]
    public void Input_that_cannot_be_read_is_a_failure_not_an_exception()
    {
        var ok = Constant(10, 0.5, 5);

        EceInterval.Assess([], [], [], 1000, 0.9, 10, "m").Error.Code.ShouldBe(EceInterval.InvalidCode);
        EceInterval.Assess(ok.Predicted, ok.Labels[..9], ok.Days, 1000, 0.9, 10, "m").IsFailure.ShouldBeTrue();
        EceInterval.Assess([.. ok.Predicted.Select(_ => 1.2)], ok.Labels, ok.Days, 1000, 0.9, 10, "m").IsFailure.ShouldBeTrue();
        EceInterval.Assess(ok.Predicted, [.. ok.Labels.Select(_ => 2)], ok.Days, 1000, 0.9, 10, "m").IsFailure.ShouldBeTrue();
        EceInterval.Assess(ok.Predicted, ok.Labels, ok.Days, 99, 0.9, 10, "m").IsFailure.ShouldBeTrue();
        EceInterval.Assess(ok.Predicted, ok.Labels, ok.Days, 1000, 0.5, 10, "m").IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void The_lean_error_equals_the_one_in_the_full_metrics()
    {
        double[] p = [0.1, 0.2, 0.2, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 0.95];
        int[] l = [0, 0, 1, 0, 1, 0, 1, 1, 1, 1];

        MetricsCalculator.EcePp(p, l, 5).ShouldBe(MetricsCalculator.Compute(p, l, 5).EcePp, 1e-12);
    }
}
