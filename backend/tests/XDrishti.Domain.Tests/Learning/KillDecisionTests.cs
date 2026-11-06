using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-12 part 12b (ADR 0219): the pure decisions around a kill rule. Only a firing rule proposes; the automatic path needs the setting ON and a proposal that is not proposal-only; a condition clears only when what fired holds now and nothing fires.</summary>
public sealed class KillDecisionTests
{
    [Theory]
    [InlineData(KillRuleState.Fires, true)]
    [InlineData(KillRuleState.Held, false)]
    [InlineData(KillRuleState.Holds, false)]
    [InlineData(KillRuleState.NotEvaluated, false)]
    public void Only_a_firing_rule_makes_a_proposal(KillRuleState state, bool proposes)
    {
        KillDecision.MakesProposal(state).ShouldBe(proposes);
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void The_automatic_path_needs_the_setting_on_and_a_proposal_that_is_not_proposal_only(bool autoOn, bool proposalOnly, bool executes)
    {
        KillDecision.MayExecuteAutomatically(autoOn, proposalOnly).ShouldBe(executes);
    }

    [Fact]
    public void A_condition_clears_only_when_every_rule_that_fired_holds_and_none_fires_or_is_held()
    {
        KillDecision.IsCleared(["cost_stress"], [("cost_stress", KillRuleState.Holds), ("drawdown", KillRuleState.NotEvaluated)]).ShouldBeTrue();
        KillDecision.IsCleared(["cost_stress"], [("cost_stress", KillRuleState.Fires)]).ShouldBeFalse();
        KillDecision.IsCleared(["cost_stress"], [("cost_stress", KillRuleState.Held)]).ShouldBeFalse();
        KillDecision.IsCleared(["cost_stress"], [("cost_stress", KillRuleState.NotEvaluated)]).ShouldBeFalse("a rule that cannot be evaluated is not a pass");
        KillDecision.IsCleared(["cost_stress"], []).ShouldBeFalse("a rule that is not reported is not a pass");
        KillDecision.IsCleared(["cost_stress"], [("cost_stress", KillRuleState.Holds), ("decay_twice", KillRuleState.Fires)]).ShouldBeFalse();
        KillDecision.IsCleared(["cost_stress", "drawdown"], [("cost_stress", KillRuleState.Holds), ("drawdown", KillRuleState.NotEvaluated)]).ShouldBeFalse();
    }

    [Fact]
    public void No_fired_rule_has_nothing_to_clear_but_a_firing_rule_still_blocks()
    {
        KillDecision.IsCleared([], [("cost_stress", KillRuleState.Holds)]).ShouldBeTrue();
        KillDecision.IsCleared([], [("cost_stress", KillRuleState.Fires)]).ShouldBeFalse();
    }

    [Fact]
    public void Kill_evidence_names_a_proposal_and_reads_its_ids_back()
    {
        KillEvidence.ForProposal(42).ShouldBe("kill-proposal:42");
        KillEvidence.NamesProposal(["run:a", "kill-proposal:42"]).ShouldBeTrue();
        KillEvidence.NamesProposal(["run:a", "cleared-proposal:42"]).ShouldBeFalse();
        KillEvidence.ProposalIds(["kill-proposal:42", "run:x", "kill-proposal:7", "kill-proposal:zz", "kill-proposal:0", "kill-proposal:-3"]).ShouldBe([42L, 7L]);
    }

    [Fact]
    public void The_cost_multiplier_of_the_options_is_used_and_the_default_is_the_specs_1_5()
    {
        KillRules.CostStress(0.05, 0.12, 100).Value!.Value.ShouldBe(-0.01, 1e-9);
        KillRules.CostStress(0.05, 0.12, 100, null, 2).Value!.Value.ShouldBe(-0.07, 1e-9);
        KillRules.CostStress(0.07, 0.12, 100, null, 2).State.ShouldBe(KillRuleState.Fires, "0.07 - 0.12 is below zero at twice the cost");
        KillRules.CostStress(0.07, 0.12, 100).State.ShouldBe(KillRuleState.Holds);
        KillRules.CostStress(0.07, 0.12, 100, null, double.NaN).State.ShouldBe(KillRuleState.NotEvaluated);
        KillRules.CostStress(0.07, 0.12, 100, null, 0.5).State.ShouldBe(KillRuleState.NotEvaluated, "a multiplier below 1 is no stress");
    }

    [Fact]
    public void The_drawdown_factor_is_an_argument_and_a_lower_factor_fires_sooner()
    {
        double[] results = [-6, -6, -6, 1]; // the largest fall is 18 R

        KillRules.Drawdown(results, 10).State.ShouldBe(KillRuleState.Holds, "18 R is not above 2 times 10 R");
        KillRules.Drawdown(results, 10, 1).State.ShouldBe(KillRuleState.Fires, "18 R is above 1 times 10 R");
        KillRules.Drawdown(results, 18, 1).State.ShouldBe(KillRuleState.Holds, "exactly equal is not above");
    }
}
