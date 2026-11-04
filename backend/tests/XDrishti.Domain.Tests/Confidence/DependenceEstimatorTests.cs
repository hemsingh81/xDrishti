using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Tests.Confidence;

public sealed class DependenceEstimatorTests
{
    private static readonly DateOnly Day0 = new(2026, 1, 5);

    private static readonly ConfidenceOptions Opts = new();

    private static (DateOnly, double) T(int day, double r) => (Day0.AddDays(day), r);

    [Fact]
    public void Fewer_than_five_trades_get_only_the_reason()
    {
        var a = DependenceEstimator.Assess([T(0, 1), T(1, 2), T(2, -1), T(3, 1)], Opts, 30, 0.15);

        a.N.ShouldBe(4);
        a.NEff.ShouldBeNull();
        a.NeededTrades.ShouldBeNull();
        a.MinTrl.ShouldBeNull();
        a.MinDetectableEdgeR.ShouldBeNull();
        a.EnoughByNEff.ShouldBeNull();
        a.Note.ShouldBe("unavailable: n < 5");
    }

    [Fact]
    public void The_block_says_which_edge_and_settings_produced_it()
    {
        var trades = Enumerable.Range(0, 12).Select(i => T(i, i % 2 == 0 ? 1.0 : 0.5)).ToList();

        var a = DependenceEstimator.Assess(trades, Opts, 30, 0.15);
        var thin = DependenceEstimator.Assess([T(0, 1)], Opts with { TargetEdgeR = 0.2 }, 30, 0.15);

        a.TargetEdgeR.ShouldBe(0.15);
        a.Settings.ShouldBe(Opts.KeyText());
        thin.TargetEdgeR.ShouldBe(0.2);
        thin.Settings.ShouldBe((Opts with { TargetEdgeR = 0.2 }).KeyText());
    }

    [Fact]
    public void A_non_finite_result_is_unavailable()
    {
        var a = DependenceEstimator.Assess([T(0, 1), T(1, 2), T(2, double.NaN), T(3, 1), T(4, 1)], Opts, 30, 0.15);

        a.NEff.ShouldBeNull();
        a.Note.ShouldContain("not a finite number");
    }

    [Fact]
    public void Thirty_trades_six_per_session_over_five_sessions_is_read_from_the_data()
    {
        var rng = new Random(3);
        var trades = new List<(DateOnly, double)>();
        for (var d = 0; d < 5; d++)
        {
            var level = (d - 2) * 0.8;
            for (var i = 0; i < 6; i++)
            {
                trades.Add(T(d * 3, level + ((rng.NextDouble() - 0.5) * 0.4)));
            }
        }

        var a = DependenceEstimator.Assess(trades, Opts, 30, 0.15);

        a.N.ShouldBe(30);
        a.Method.ShouldBe("cluster");
        a.NEff!.Value.ShouldBeInRange(1, 30);
        a.NEff!.Value.ShouldBeLessThan(10);   // strongly clustered
        a.DesignEffect!.Value.ShouldBe(30 / a.NEff!.Value, 1e-9);
        a.EnoughByNEff.ShouldBe(false);
        a.NeededTrades.ShouldNotBeNull();
        a.MinDetectableEdgeR.ShouldNotBeNull();
        a.SpanYears!.Value.ShouldBe(13 / 365.25, 1e-9);
    }

    [Fact]
    public void The_power_numbers_use_the_floor_and_the_target_edge()
    {
        // a sample SD of 0.3 reads sigma 0.5: ((1.6449 + 0.8416) * 0.5 / 0.15)^2 = 68.7 -> 69
        var trades = Enumerable.Range(0, 12).Select(i => T(i, 0.3 * ((i % 2 * 2) - 1))).ToList();

        var a = DependenceEstimator.Assess(trades, Opts, 30, 0.15);

        a.NeededTrades.ShouldBe(69);
    }

    [Fact]
    public void The_order_of_the_input_does_not_matter()
    {
        var rng = new Random(11);
        var trades = Enumerable.Range(0, 60).Select(i => T(rng.Next(12), (rng.NextDouble() * 3) - 1)).ToList();

        var a = DependenceEstimator.Assess(trades, Opts, 30, 0.15);
        var b = DependenceEstimator.Assess([.. trades.AsEnumerable().Reverse()], Opts, 30, 0.15);

        b.NEff!.Value.ShouldBe(a.NEff!.Value, 1e-9);   // a different order of the same numbers may differ in the last bits
        b.DesignEffect!.Value.ShouldBe(a.DesignEffect!.Value, 1e-9);
        b.NeededTrades.ShouldBe(a.NeededTrades);
        b.MinTrl.ShouldBe(a.MinTrl);
        b.Method.ShouldBe(a.Method);
    }

    [Fact]
    public void Min_trl_says_normal_assumed_below_twenty_trades_and_no_edge_when_the_mean_is_not_positive()
    {
        var losing = DependenceEstimator.Assess(Enumerable.Range(0, 12).Select(i => T(i, i % 2 == 0 ? -1.0 : 0.5)).ToList(), Opts, 30, 0.15);
        var winning = DependenceEstimator.Assess(Enumerable.Range(0, 12).Select(i => T(i, i % 2 == 0 ? 1.0 : 0.5)).ToList(), Opts, 30, 0.15);

        losing.MinTrl.ShouldBeNull();
        losing.Note.ShouldContain("no edge to detect");
        winning.MinTrl.ShouldNotBeNull();
        winning.NormalAssumed.ShouldBeTrue();
        winning.Note.ShouldContain("normal assumed");
        winning.Note.ShouldContain("noisy");
    }

    [Fact]
    public void Min_trl_dependent_is_min_trl_times_the_design_effect()
    {
        var trades = Enumerable.Range(0, 40).Select(i => T(i / 8, (i % 8 * 0.1) + (i / 8 * 0.3))).ToList();

        var a = DependenceEstimator.Assess(trades, Opts, 30, 0.15);

        a.MinTrl.ShouldNotBeNull();
        a.MinTrlDependent.ShouldBe((int)Math.Ceiling(a.MinTrl!.Value * a.DesignEffect!.Value - 1e-9));
        a.NormalAssumed.ShouldBeFalse();
    }
}
