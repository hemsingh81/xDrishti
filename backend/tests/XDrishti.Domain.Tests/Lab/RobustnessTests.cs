using XDrishti.Domain.Lab;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>Spec P5-05, criteria 3 to 5: the seeded Monte Carlo and baseline, by year, the sweep plan and the judgement of a sweep.</summary>
public sealed class RobustnessTests
{
    private static readonly double[] Results = [2, -1, 2, -1, -1, 2, -1, 2, 2, -1];

    [Fact]
    public void The_generator_gives_the_same_numbers_for_a_seed_and_stays_in_range()
    {
        var a = new SeededRandom(7);
        var b = new SeededRandom(7);

        var drawn = Enumerable.Range(0, 100).Select(_ => a.Next(5)).ToList();

        drawn.ShouldBe(Enumerable.Range(0, 100).Select(_ => b.Next(5)).ToList());
        drawn.ShouldAllBe(v => v >= 0 && v < 5);
        drawn.Distinct().Count().ShouldBe(5);
    }

    [Fact]
    public void The_seed_of_a_run_is_stable_and_differs_by_key()
    {
        SeededRandom.SeedOf("a").ShouldBe(SeededRandom.SeedOf("a"));
        SeededRandom.SeedOf("a").ShouldNotBe(SeededRandom.SeedOf("b"));
    }

    [Fact]
    public void Monte_carlo_is_deterministic_for_a_seed_and_differs_for_another()
    {
        var one = MonteCarlo.Run(Results, 500, 1);

        one.ShouldBe(MonteCarlo.Run(Results, 500, 1));
        one.ShouldNotBe(MonteCarlo.Run(Results, 500, 2));
    }

    [Fact]
    public void Monte_carlo_reports_the_observed_figures_and_ordered_percentiles()
    {
        var mc = MonteCarlo.Run(Results, 1000, 5)!;

        mc.ObservedMeanR.ShouldBe(0.5, 1e-9);
        mc.ObservedMaxDrawdownR.ShouldBe(2, 1e-9); // cumulative 2 1 3 2 1 3 2 4 6 5: the fall from 3 to 1
        mc.MeanP05.ShouldBeLessThanOrEqualTo(mc.MeanP50);
        mc.MeanP50.ShouldBeLessThanOrEqualTo(mc.MeanP95);
        mc.DrawdownP50.ShouldBeLessThanOrEqualTo(mc.DrawdownP95);
        mc.ShareNotPositive.ShouldBeInRange(0, 1);
        mc.MeanP50.ShouldBeInRange(0.0, 1.0);
    }

    [Fact]
    public void Monte_carlo_of_all_wins_never_loses_and_all_losses_always_does()
    {
        MonteCarlo.Run([1, 1, 1], 100, 3)!.ShareNotPositive.ShouldBe(0);
        MonteCarlo.Run([-1, -1, -1], 100, 3)!.ShareNotPositive.ShouldBe(1);
        MonteCarlo.Run([-1, -1, -1], 100, 3)!.DrawdownP50.ShouldBe(3);
    }

    [Fact]
    public void Fewer_than_two_trades_give_no_monte_carlo_and_no_baseline()
    {
        MonteCarlo.Run([1], 100, 1).ShouldBeNull();
        SignFlipBaseline.Run([], 100, 1).ShouldBeNull();
    }

    [Fact]
    public void Max_drawdown_is_the_largest_fall_from_a_peak()
    {
        MonteCarlo.MaxDrawdown([1, 1, -2, -1, 3, -1]).ShouldBe(3, 1e-9);
        MonteCarlo.MaxDrawdown([-1, -1]).ShouldBe(2, 1e-9);
    }

    [Fact]
    public void The_sign_flip_p_value_is_small_for_a_strong_edge_large_for_none_and_never_zero()
    {
        var strong = SignFlipBaseline.Run(Enumerable.Repeat(1.0, 20).ToList(), 999, 4)!;
        var none = SignFlipBaseline.Run([1, -1, 1, -1], 999, 4)!;

        strong.PValue.ShouldBeLessThan(0.01);
        strong.PValue.ShouldBeGreaterThan(0);
        none.PValue.ShouldBeGreaterThan(0.3);
        strong.ShouldBe(SignFlipBaseline.Run(Enumerable.Repeat(1.0, 20).ToList(), 999, 4));
    }

    private static SelectionTrade T(int y, int m, double r) => new("c", new DateOnly(y, m, 5), new DateOnly(y, m, 6), r);

    [Fact]
    public void By_year_groups_by_entry_year_with_exact_figures()
    {
        var rows = YearBreakdown.Of([T(2026, 1, 2), T(2025, 12, -1), T(2026, 2, -1), T(2025, 11, -2)]);

        rows.Select(r => r.Year).ShouldBe([2025, 2026]);
        rows[0].ShouldBe(new YearRow(2025, 2, -1.5, -3, 0, false));
        rows[1].ShouldBe(new YearRow(2026, 2, 0.5, 1, 2, true));
        YearBreakdown.Of([T(2026, 1, 1)])[0].ProfitFactor.ShouldBeNull();
    }

    [Fact]
    public void The_sweep_plan_takes_four_parameters_rounds_whole_ones_and_drops_duplicates_and_the_declared_value()
    {
        var declared = new List<KeyValuePair<string, double>>
        {
            new("period", 14), new("mult", 1.5), new("a", 2), new("b", 3), new("c", 4),
        };

        var plan = SweepPlan.Build(declared, 4, [0.8, 0.9, 1.1, 1.2]);

        plan.Select(p => p.Parameter).Distinct().ShouldBe(["period", "mult", "b"]);
        plan.Where(p => p.Parameter == "period").Select(p => p.Value).ShouldBe([11, 13, 15, 17]);
        plan.Where(p => p.Parameter == "mult").Select(p => p.Value).ShouldBe([1.2, 1.35, 1.65, 1.8]);
        plan.Where(p => p.Parameter == "a").ShouldBeEmpty("every step rounds back to the declared 2");
        plan.Where(p => p.Parameter == "b").Select(p => p.Value).ShouldBe([2, 4]);
    }

    [Fact]
    public void The_sweep_plan_is_capped_at_sixteen_points_and_by_the_parameter_limit()
    {
        var declared = Enumerable.Range(0, 6).Select(i => new KeyValuePair<string, double>("p" + i, 100)).ToList();

        SweepPlan.Build(declared, 4, [0.5, 0.6, 0.7, 0.8, 1.3]).Count.ShouldBe(16);
        SweepPlan.Build(declared, 1, [0.5]).Count.ShouldBe(1);
        SweepPlan.Build([], 4, [0.5]).ShouldBeEmpty();
    }

    private static SweepResult R(string p, double v, double? mean, string? error = null) => new(new SweepPoint(p, v), 10, mean, null, null, error);

    [Fact]
    public void A_sweep_is_judged_stable_fragile_without_an_edge_or_inconclusive()
    {
        var baseline = R(string.Empty, 0, 0.4);

        var judged = SensitivityAnalysis.Judge(baseline, [R("a", 1, 0.5), R("a", 2, 0.3), R("b", 1, 0.5), R("b", 2, 0.1), R("c", 1, 0.5), R("c", 2, null, "refused")]);

        judged.Single(j => j.Parameter == "a").Verdict.ShouldBe("stable");
        judged.Single(j => j.Parameter == "b").Verdict.ShouldBe("fragile");
        judged.Single(j => j.Parameter == "c").Verdict.ShouldBe("inconclusive");
        SensitivityAnalysis.Judge(R(string.Empty, 0, -0.1), [R("a", 1, 0.5), R("a", 2, 0.3)]).Single().Verdict.ShouldBe("no_base_edge");
        SensitivityAnalysis.Judge(R(string.Empty, 0, null, "boom"), [R("a", 1, 0.5), R("a", 2, 0.3)]).Single().Reason.ShouldContain("boom");
    }
}
