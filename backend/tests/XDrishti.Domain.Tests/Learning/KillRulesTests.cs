using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-12 part 12a, AC3 and AC4 (ADR 0218, spec D2): the kill rules evaluate and return a verdict with numbers; they act on nothing.</summary>
public sealed class KillRulesTests
{
    [Theory]
    [InlineData(0.05, -0.01, KillRuleState.Fires)]
    [InlineData(0.07, 0.01, KillRuleState.Holds)]
    [InlineData(0.06, 0.00, KillRuleState.Holds)] // exactly zero: the test is strictly negative
    public void Cost_stress_by_hand_with_a_cost_of_0_12(double net1, double stressed, KillRuleState state)
    {
        var verdict = KillRules.CostStress(net1, 0.12, 100);

        verdict.State.ShouldBe(state);
        verdict.Value!.Value.ShouldBe(stressed, 1e-9);
        verdict.Limit.ShouldBe(0);
        verdict.Rule.ShouldBe("cost_stress");
        verdict.ProposalOnly.ShouldBeFalse();
    }

    [Fact]
    public void Cost_stress_reads_only_the_last_hundred_trades_of_the_list()
    {
        // 30 older trades that would keep, then 100 that fire: net 0.05 and cost 0.12 on the last 100
        var net = Enumerable.Repeat(1.0, 30).Concat(Enumerable.Repeat(0.05, 100)).ToList();
        var cost = Enumerable.Repeat(0.0, 30).Concat(Enumerable.Repeat(0.12, 100)).ToList();

        var verdict = KillRules.CostStress(net, cost);

        verdict.State.ShouldBe(KillRuleState.Fires);
        verdict.Trades.ShouldBe(100);
        verdict.Value!.Value.ShouldBe(-0.01, 1e-9);
    }

    [Fact]
    public void Fewer_than_a_hundred_trades_are_not_evaluated_and_never_a_demotion()
    {
        var verdict = KillRules.CostStress([.. Enumerable.Repeat(-5.0, 99)], [.. Enumerable.Repeat(1.0, 99)]);

        verdict.State.ShouldBe(KillRuleState.NotEvaluated);
        verdict.Value.ShouldBeNull();
        verdict.Text.ShouldContain("99");
    }

    [Fact]
    public void A_figure_that_is_not_a_number_is_not_evaluated()
    {
        KillRules.CostStress([.. Enumerable.Repeat(double.NaN, 100)], [.. Enumerable.Repeat(0.1, 100)]).State.ShouldBe(KillRuleState.NotEvaluated);
        KillRules.CostStress(double.NaN, 0.1, 100).State.ShouldBe(KillRuleState.NotEvaluated);
        Should.Throw<ArgumentException>(() => KillRules.CostStress([1.0], []));
    }

    [Fact]
    public void A_negative_cost_is_not_evaluated_never_a_stress_that_helps()
    {
        var net = Enumerable.Repeat(0.05, 100).ToList();
        var cost = Enumerable.Repeat(0.12, 100).ToList();
        cost[40] = -0.01; // gross R below net R

        KillRules.CostStress(net, cost).State.ShouldBe(KillRuleState.NotEvaluated);
        KillRules.CostStress(net, cost).Text.ShouldContain("negative cost");
        KillRules.CostStress(0.05, -0.02, 100).State.ShouldBe(KillRuleState.NotEvaluated);
    }

    [Theory]
    [InlineData(19, KillRuleState.Held)]
    [InlineData(20, KillRuleState.Fires)]
    [InlineData(0, KillRuleState.Held)]
    public void A_rule_that_fired_needs_twenty_new_trades_before_it_fires_again(int since, KillRuleState expected)
    {
        KillRules.CostStress(0.05, 0.12, 100, since).State.ShouldBe(expected);
    }

    [Fact]
    public void A_rule_that_never_fired_has_no_wait_and_a_keep_is_not_held()
    {
        KillRules.CostStress(0.05, 0.12, 100, null).State.ShouldBe(KillRuleState.Fires);
        KillRules.CostStress(0.07, 0.12, 100, 3).State.ShouldBe(KillRuleState.Holds);
    }

    [Fact]
    public void The_drawdown_rule_demotes_above_two_times_the_percentile_and_not_at_it()
    {
        // the 95th percentile of the matched drawdown at this series' n is 8 R: the limit is 16
        KillRules.Drawdown([-17.0], 8).State.ShouldBe(KillRuleState.Fires);
        KillRules.Drawdown([-16.0], 8).State.ShouldBe(KillRuleState.Holds);
        KillRules.Drawdown([-17.0], 8).Limit.ShouldBe(16);
        KillRules.Drawdown([-17.0], 8).Value.ShouldBe(17);
    }

    [Fact]
    public void The_drawdown_is_the_largest_peak_to_trough_fall_of_the_cumulative_r()
    {
        // +5, -3, -4 (fall 7 from 5 to -2), +1
        var verdict = KillRules.Drawdown([5.0, -3.0, -4.0, 1.0], 3);

        verdict.Value.ShouldBe(7);
        verdict.Limit.ShouldBe(6);
        verdict.State.ShouldBe(KillRuleState.Fires);
        verdict.Text.ShouldContain("the factor 2 is a proposal"); // the factor is stated next to the number
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(0.0)]
    public void The_drawdown_rule_without_a_usable_percentile_is_not_evaluated(double? p95)
    {
        KillRules.Drawdown([-50.0], p95).State.ShouldBe(KillRuleState.NotEvaluated);
    }

    [Fact]
    public void The_drawdown_rule_with_no_trade_is_not_evaluated()
    {
        KillRules.Drawdown([], 8).State.ShouldBe(KillRuleState.NotEvaluated);
    }

    private static DecayReport Report(int windows, int streak) => new(
        windows * 60, 0, [.. Enumerable.Range(1, windows).Select(i => new DecayWindow(i, 60, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1), 0, 1, 0, false))], streak, streak >= 2, null, [], false, [], null);

    [Theory]
    [InlineData(3, 2, KillRuleState.Fires)]
    [InlineData(3, 1, KillRuleState.Holds)]
    [InlineData(2, 0, KillRuleState.Holds)]
    [InlineData(1, 1, KillRuleState.NotEvaluated)]
    [InlineData(0, 0, KillRuleState.NotEvaluated)]
    public void Decay_twice_in_a_row_reads_the_streak_of_completed_windows(int windows, int streak, KillRuleState expected)
    {
        KillRules.DecayTwice(Report(windows, streak)).State.ShouldBe(expected);
    }

    [Fact]
    public void The_failure_boundary_is_a_fourth_input_and_proposal_only()
    {
        var fires = KillRules.FailureBoundary(SequentialState.Failure, -3.1, -2.94, 40);
        var holds = KillRules.FailureBoundary(SequentialState.Caution, -1.7, -2.94, 40);
        var none = KillRules.FailureBoundary(null, null, -2.94, 0);

        fires.State.ShouldBe(KillRuleState.Fires);
        fires.ProposalOnly.ShouldBeTrue();
        holds.State.ShouldBe(KillRuleState.Holds);
        holds.ProposalOnly.ShouldBeTrue();
        none.State.ShouldBe(KillRuleState.NotEvaluated);
        none.ProposalOnly.ShouldBeTrue();
    }

    [Fact]
    public void An_unset_verdict_state_is_not_evaluated()
    {
        default(KillRuleState).ShouldBe(KillRuleState.NotEvaluated);
    }
}
