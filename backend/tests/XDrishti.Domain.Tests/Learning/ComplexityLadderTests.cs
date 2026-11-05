using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-10 part 10b (ADR 0215), spec AC4: a rung is earned only against the rung below, within the events-per-variable cap, and a missing figure is never a pass.</summary>
public sealed class ComplexityLadderTests
{
    private static readonly LadderOptions Options = new();

    [Fact]
    public void The_base_rate_needs_nothing()
    {
        var a = ComplexityLadder.Assess("base", LadderRung.BaseRate, 0, 0, null, Options);

        a.Earned.ShouldBeTrue();
        a.Reasons.ShouldBeEmpty();
    }

    [Fact]
    public void A_rung_that_does_not_reach_95_percent_over_the_rung_below_is_refused_with_the_reason()
    {
        var a = ComplexityLadder.Assess("cell", LadderRung.CellRate, 200, 0, 0.9258, Options);

        a.Earned.ShouldBeFalse();
        a.Reasons.ShouldBe(["the probability of beating the base rate is 0.9258, below 0.95"]);
    }

    [Fact]
    public void A_rung_at_exactly_95_percent_is_earned()
    {
        ComplexityLadder.Assess("cell", LadderRung.CellRate, 200, 0, 0.95, Options).Earned.ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    public void A_missing_probability_is_not_a_pass(double? p)
    {
        var a = ComplexityLadder.Assess("cell", LadderRung.CellRate, 200, 0, p, Options);

        a.Earned.ShouldBeFalse();
        a.ProbabilityBeatsBelow.ShouldBeNull();
        a.Reasons.Single().ShouldContain("not available");
    }

    [Fact]
    public void Trees_with_99_events_are_refused_and_with_100_are_earned()
    {
        var refused = ComplexityLadder.Assess("m", LadderRung.Trees, 99, 40, 0.99, Options);
        var earned = ComplexityLadder.Assess("m", LadderRung.Trees, 100, 40, 0.99, Options);

        refused.Earned.ShouldBeFalse();
        refused.Reasons.ShouldBe(["99 events, 100 needed"]);
        earned.Earned.ShouldBeTrue();
    }

    [Fact]
    public void A_logistic_model_with_5_features_and_48_events_is_refused_for_events_per_variable()
    {
        var a = ComplexityLadder.Assess("logistic", LadderRung.Logistic, 48, 5, 0.99, Options);

        a.Earned.ShouldBeFalse();
        a.Reasons.ShouldBe(["5 features exceed the cap of 3 (48 events at 15 per variable, at most 8)"]);
    }

    [Fact]
    public void A_logistic_model_within_the_cap_is_earned_and_one_over_the_hard_limit_is_not()
    {
        ComplexityLadder.Assess("logistic", LadderRung.Logistic, 48, 3, 0.99, Options).Earned.ShouldBeTrue();
        ComplexityLadder.Assess("logistic", LadderRung.Logistic, 5000, 9, 0.99, Options).Earned.ShouldBeFalse();
    }

    [Fact]
    public void Every_reason_is_listed_in_a_fixed_order()
    {
        var a = ComplexityLadder.Assess("logistic", LadderRung.Logistic, 10, 41, null, Options);

        a.Reasons.Count.ShouldBe(2);
        a.Reasons[0].ShouldContain("not available");
        a.Reasons[1].ShouldContain("exceed the cap of 0");
    }

    [Fact]
    public void The_settings_move_the_cuts()
    {
        var loose = new LadderOptions(Epv: 10, MinTreeEvents: 40, MinProbability: 0.9);

        ComplexityLadder.Assess("logistic", LadderRung.Logistic, 48, 4, 0.91, loose).Earned.ShouldBeTrue();
        ComplexityLadder.Assess("m", LadderRung.Trees, 40, 4, 0.91, loose).Earned.ShouldBeTrue();
    }

    [Fact]
    public void A_structural_reason_keeps_a_rung_unearned_whatever_the_numbers_and_comes_last()
    {
        var a = ComplexityLadder.Assess("m", LadderRung.Trees, 500, 40, 0.99, Options, "not monotone");
        var b = ComplexityLadder.Assess("m", LadderRung.Trees, 50, 40, 0.99, Options, "not monotone");

        a.Earned.ShouldBeFalse();
        a.Reasons.ShouldBe(["not monotone"]);
        b.Reasons.ShouldBe(["50 events, 100 needed", "not monotone"]);
        ComplexityLadder.Assess("m", LadderRung.Trees, 500, 40, 0.99, Options).Earned.ShouldBeTrue();
    }
}
