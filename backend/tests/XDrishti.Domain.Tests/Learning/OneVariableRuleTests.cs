using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>The one-variable rule (spec P6-10) on the definitions two stored runs carry.</summary>
public sealed class OneVariableRuleTests
{
    private const string Fills = "{\"entry_limit_percent\":0.1,\"slippage\":{\"gap_fill\":\"open\",\"ticks\":1,\"bar_range_frac\":0.05,\"stop_exit_extra_ticks\":1}}";

    private const string Dearer = "{\"entry_limit_percent\":0.1,\"slippage\":{\"gap_fill\":\"open\",\"ticks\":2,\"bar_range_frac\":0.05,\"stop_exit_extra_ticks\":1}}";

    private static RunDefinition Def(string rest = "{\"id\":\"ORB\"}", string exits = "buy=[];sell=[];", string fills = Fills, params (string Name, double Value)[] parameters) =>
        new("ORB", parameters.ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal), rest, exits, fills);

    private static RunDefinition Base(params (string Name, double Value)[] extra) => Def(parameters: [("or_min", 15), ("rr", 2), .. extra]);

    private static ExperimentKnob Rr(string from = "2", string to = "2.5") => new("rr", ExperimentKnobKind.Parameter, from, to);

    [Fact]
    public void A_candidate_that_changes_only_the_named_parameter_passes()
    {
        OneVariableRule.Problems(Rr(), Base(), Def(parameters: [("or_min", 15), ("rr", 2.5)])).ShouldBeEmpty();
    }

    [Fact]
    public void Two_changed_parameters_are_refused_and_named()
    {
        var problems = OneVariableRule.Problems(Rr(), Base(), Def(parameters: [("or_min", 20), ("rr", 2.5)]));

        problems.Single().ShouldContain("2 parameters differ (or_min, rr)");
    }

    [Fact]
    public void A_different_parameter_than_the_named_knob_is_refused()
    {
        OneVariableRule.Problems(Rr(), Base(), Def(parameters: [("or_min", 20), ("rr", 2)])).Single().ShouldContain("only 'rr'");
    }

    [Fact]
    public void An_unchanged_candidate_is_refused()
    {
        OneVariableRule.Problems(Rr(), Base(), Base()).Single().ShouldContain("did not change");
    }

    [Fact]
    public void The_stored_values_must_be_the_stated_ones()
    {
        var problems = OneVariableRule.Problems(Rr("3", "4"), Base(), Def(parameters: [("or_min", 15), ("rr", 2.5)]));

        problems.Count.ShouldBe(2);
    }

    [Fact]
    public void A_parameter_that_exists_in_one_run_only_counts_as_changed()
    {
        OneVariableRule.Problems(Rr(), Base(), Def(parameters: [("or_min", 15), ("rr", 2.5), ("extra", 1)])).Single().ShouldContain("2 parameters differ");
    }

    [Fact]
    public void A_parameter_change_with_changed_exits_or_fills_or_definition_is_refused()
    {
        var changed = Def(parameters: [("or_min", 15), ("rr", 2.5)]);

        OneVariableRule.Problems(Rr(), Base(), changed with { Exits = "buy=[1];sell=[];" }).Single().ShouldContain("exits differ");
        OneVariableRule.Problems(Rr(), Base(), changed with { Fills = Dearer }).Single().ShouldContain("fill rules differ");
        OneVariableRule.Problems(Rr(), Base(), changed with { Rest = "{\"id\":\"ORB\",\"filters\":[]}" }).Single().ShouldContain("outside its parameters");
    }

    [Fact]
    public void An_exit_knob_changes_only_the_exits_and_its_values_are_named_in_the_stored_exits()
    {
        var knob = new ExperimentKnob("exit", ExperimentKnobKind.Exit, "fixed_rr", "partial_be");
        var fixedRr = Base() with { Exits = "buy=[{\"policy\":\"fixed_rr\"}];sell=[];" };
        var partial = Base() with { Exits = "buy=[{\"policy\":\"partial_be\"}];sell=[];" };

        OneVariableRule.Problems(knob, fixedRr, partial).ShouldBeEmpty();
        OneVariableRule.Problems(knob, fixedRr, fixedRr).ShouldContain("the exits are the same: the knob did not change");
        OneVariableRule.Problems(knob, fixedRr, partial with { Exits = "buy=[{\"policy\":\"other\"}];sell=[];" }).Single().ShouldContain("candidate run do not name 'partial_be'");
        OneVariableRule.Problems(knob with { BaselineValue = "trail" }, fixedRr, partial).Single().ShouldContain("baseline run do not name 'trail'");
        OneVariableRule.Problems(knob, fixedRr, Def(exits: partial.Exits, parameters: [("or_min", 15), ("rr", 3)])).Single().ShouldContain("parameters differ (rr)");
        OneVariableRule.BaselineProblems(knob, Base()).Single().ShouldContain("do not name");
        OneVariableRule.BaselineProblems(knob, fixedRr).ShouldBeEmpty();
    }

    [Fact]
    public void A_candidate_without_the_knob_parameter_is_a_problem_not_an_exception()
    {
        var problems = OneVariableRule.Problems(Rr(), Base(), Def(parameters: [("or_min", 15)]));

        problems.ShouldContain("the candidate run has no parameter 'rr'");
        OneVariableRule.Problems(Rr(), Def(parameters: [("or_min", 15)]), Base()).ShouldContain("the baseline run has no parameter 'rr'");
    }

    [Fact]
    public void A_slippage_knob_changes_only_the_slippage_fields()
    {
        var knob = new ExperimentKnob("slippage", ExperimentKnobKind.Slippage, "1", "2");

        OneVariableRule.Problems(knob, Base(), Base() with { Fills = Dearer }).ShouldBeEmpty();
        OneVariableRule.Problems(knob, Base(), Base()).Single().ShouldContain("same slippage");
        OneVariableRule.Problems(knob, Base(), Base() with { Fills = Dearer, Exits = "buy=[1];sell=[];" }).Single().ShouldContain("exits differ");
        OneVariableRule.Problems(knob, Base(), Base() with { Fills = Dearer.Replace("0.1,", "0.2,", StringComparison.Ordinal) }).Single().ShouldContain("entry limit");
    }

    [Fact]
    public void A_different_strategy_is_refused()
    {
        OneVariableRule.Problems(Rr(), Base(), Def(parameters: [("or_min", 15), ("rr", 2.5)]) with { StrategyId = "VWAP" }).Single().ShouldContain("strategy differs");
    }

    [Fact]
    public void The_baseline_must_carry_a_parameter_knob_at_its_stored_value()
    {
        OneVariableRule.BaselineProblems(Rr(), Base()).ShouldBeEmpty();
        OneVariableRule.BaselineProblems(Rr("3", "4"), Base()).Single().ShouldContain("used 2");
        OneVariableRule.BaselineProblems(new ExperimentKnob("nope", ExperimentKnobKind.Parameter, "1", "2"), Base()).Single().ShouldContain("no parameter");
        OneVariableRule.BaselineProblems(new ExperimentKnob("slippage", ExperimentKnobKind.Slippage, "1", "2"), Base()).ShouldBeEmpty();
    }
}
