using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Tests.Lab;

/// <summary>P5-03 criteria 4 to 8: the adjustment factor, the gates on hand tables, the outcomes, the honest limits and determinism.</summary>
public sealed class VerdictEvaluatorTests
{
    private static readonly QualificationGates Gates = QualificationGates.Default;

    private static OosEvidence Good(string method = "walk_forward", int folds = 12) =>
        new(100, 0.40, 1.8, folds, folds * 3 / 4, 4, 2, method);

    private static Verdict Judge(OosEvidence e, int n = 1, int minFolds = 6) => VerdictEvaluator.Evaluate(Gates, e, n, minFolds);

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(1, 1.0)]
    [InlineData(2, 1.25)]
    [InlineData(4, 1.5)]
    [InlineData(16, 2.0)]
    public void The_factor_follows_one_plus_a_quarter_of_log2_of_the_variants(int n, double factor) =>
        MultipleTesting.Factor(n).ShouldBe(factor, 1e-12);

    [Fact]
    public void With_the_adjustment_off_the_effective_count_is_one_and_the_factor_one()
    {
        MultipleTesting.Effective(16, false).ShouldBe(1);
        MultipleTesting.Effective(16, true).ShouldBe(16);
        MultipleTesting.Factor(MultipleTesting.Effective(16, false)).ShouldBe(1.0);
    }

    [Fact]
    public void Every_gate_met_by_walk_forward_with_enough_folds_is_a_pass_and_each_gate_has_a_reason_with_its_numbers()
    {
        var v = Judge(Good());

        v.Outcome.ShouldBe(VerdictOutcome.Pass);
        v.Limits.ShouldBeEmpty();
        v.Gates.Select(g => g.Name).ShouldBe(["oos_trades", "expectancy_R", "profit_factor", "positive_folds_pct", "max_drawdown_R", "calibration_error_pp"]);
        v.Gates.ShouldAllBe(g => g.Passed && g.Reason.Contains("met", StringComparison.Ordinal));
        v.Gates[0].Reason.ShouldContain("100");
        v.Gates[0].Reason.ShouldContain("30");
    }

    [Theory]
    [InlineData(25, 0.40, 1.8, 4, 2)]
    [InlineData(100, 0.13, 1.8, 4, 2)]
    [InlineData(100, 0.40, 1.15, 4, 2)]
    [InlineData(100, 0.40, 1.8, 11, 2)]
    [InlineData(100, 0.40, 1.8, 4, 6)]
    public void One_gate_missed_slightly_is_marginal(int trades, double exp, double pf, double dd, double cal)
    {
        var v = Judge(new OosEvidence(trades, exp, pf, 12, 9, dd, cal, "walk_forward"));

        v.Outcome.ShouldBe(VerdictOutcome.Marginal);
        v.Gates.Count(g => !g.Passed).ShouldBe(1);
        v.Gates.Single(g => !g.Passed).Reason.ShouldContain("missed");
    }

    [Theory]
    [InlineData(14, 0.40, 1.8, 4, 2)]
    [InlineData(100, 0.05, 1.8, 4, 2)]
    [InlineData(100, 0.40, 0.85, 4, 2)]
    [InlineData(100, 0.40, 1.8, 14, 2)]
    [InlineData(100, 0.40, 1.8, 4, 7)]
    [InlineData(0, double.NaN, double.NaN, 0, 0)]
    public void A_gate_missed_by_more_than_a_quarter_or_trades_below_half_is_a_fail(int trades, double exp, double pf, double dd, double cal) =>
        Judge(new OosEvidence(trades, exp, pf, 12, 9, dd, cal, "walk_forward")).Outcome.ShouldBe(VerdictOutcome.Fail);

    [Fact]
    public void Positive_folds_below_the_share_fail_hard_when_far_below()
    {
        Judge(new OosEvidence(100, 0.4, 1.8, 12, 6, 4, 2, "walk_forward")).Outcome.ShouldBe(VerdictOutcome.Marginal);
        Judge(new OosEvidence(100, 0.4, 1.8, 12, 3, 4, 2, "walk_forward")).Outcome.ShouldBe(VerdictOutcome.Fail);
    }

    [Fact]
    public void The_tightening_is_visible_the_same_evidence_passes_at_one_variant_and_does_not_at_sixteen()
    {
        var evidence = new OosEvidence(40, 0.20, 1.3, 12, 9, 4, 2, "walk_forward");

        Judge(evidence, 1).Outcome.ShouldBe(VerdictOutcome.Pass);
        var sixteen = Judge(evidence, 16);
        sixteen.Outcome.ShouldNotBe(VerdictOutcome.Pass);
        sixteen.VariantCount.ShouldBe(16);
        sixteen.AdjustmentFactor.ShouldBe(2.0, 1e-12);
    }

    [Theory]
    [InlineData(1, 30, 0.15, 1.2)]
    [InlineData(2, 38, 0.1875, 1.25)]
    [InlineData(4, 45, 0.225, 1.3)]
    [InlineData(16, 60, 0.30, 1.4)]
    public void The_adjusted_thresholds_are_printed_beside_the_configured_ones(int n, int trades, double exp, double pf)
    {
        var v = Judge(Good(), n);

        v.Gates[0].Adjusted.ShouldBe(trades);
        v.Gates[0].Required.ShouldBe(30);
        v.Gates[1].Adjusted.ShouldBe(exp, 1e-9);
        v.Gates[2].Adjusted.ShouldBe(pf, 1e-9);
        v.Gates[3].Adjusted.ShouldBe(60);
        v.Gates[4].Adjusted.ShouldBe(10);
        v.Gates[5].Adjusted.ShouldBe(5);
        v.Gates[0].Reason.ShouldContain(trades.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("full_period")]
    [InlineData("quick")]
    public void A_method_other_than_walk_forward_never_passes_and_says_so(string method)
    {
        var v = Judge(Good(method));

        v.Outcome.ShouldBe(VerdictOutcome.Marginal);
        v.Limits.ShouldContain(l => l.StartsWith("exploration, not evidence", StringComparison.Ordinal));
    }

    [Fact]
    public void Five_folds_cap_a_good_result_at_marginal_with_the_count_and_six_can_pass()
    {
        var five = Judge(new OosEvidence(100, 0.4, 1.8, 5, 4, 4, 2, "walk_forward"));
        var six = Judge(new OosEvidence(100, 0.4, 1.8, 6, 5, 4, 2, "walk_forward"));

        five.Outcome.ShouldBe(VerdictOutcome.Marginal);
        five.Limits.Single().ShouldContain("5 folds");
        six.Outcome.ShouldBe(VerdictOutcome.Pass);
    }

    [Fact]
    public void Both_limits_can_apply_and_a_fail_stays_a_fail()
    {
        Judge(new OosEvidence(100, 0.4, 1.8, 3, 3, 4, 2, "full_period")).Limits.Count.ShouldBe(2);
        Judge(new OosEvidence(0, 0, 0, 0, 0, 0, 0, "full_period")).Outcome.ShouldBe(VerdictOutcome.Fail);
    }

    [Fact]
    public void A_zero_threshold_is_met_by_zero_and_missed_by_less()
    {
        var zero = new QualificationGates(0, 0, 1, 0, 10, 5);
        var ok = VerdictEvaluator.Evaluate(zero, new OosEvidence(0, 0, 1, 6, 0, 0, 0, "walk_forward"), 1, 6);
        var low = VerdictEvaluator.Evaluate(zero, new OosEvidence(0, -0.01, 1, 6, 0, 0, 0, "walk_forward"), 1, 6);

        ok.Outcome.ShouldBe(VerdictOutcome.Pass);
        low.Outcome.ShouldBe(VerdictOutcome.Fail);
    }

    [Fact]
    public void An_infinite_profit_factor_passes_and_a_negative_one_fails()
    {
        Judge(Good() with { ProfitFactor = double.PositiveInfinity }).Outcome.ShouldBe(VerdictOutcome.Pass);
        Judge(Good() with { ProfitFactor = -1 }).Outcome.ShouldBe(VerdictOutcome.Fail);
    }

    [Fact]
    public void The_same_inputs_give_the_same_verdict_every_time()
    {
        var rng = new Random(7);
        for (var i = 0; i < 200; i++)
        {
            var e = new OosEvidence(rng.Next(0, 200), rng.NextDouble(), rng.NextDouble() * 3, rng.Next(0, 13), rng.Next(0, 6), rng.NextDouble() * 15, rng.NextDouble() * 10, i % 3 == 0 ? "quick" : "walk_forward");
            var n = rng.Next(0, 40);

            var a = Judge(e, n);
            var b = Judge(e, n);

            b.Outcome.ShouldBe(a.Outcome);
            b.Gates.ShouldBe(a.Gates);
            b.Limits.ShouldBe(a.Limits);
        }
    }
}
