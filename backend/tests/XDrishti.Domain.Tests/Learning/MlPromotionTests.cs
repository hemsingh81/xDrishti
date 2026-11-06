using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Domain.Tests.Learning;

/// <summary>CF-10 part 10d-1, D7 and AC11 (ADR 0220): the promotion rule's truth table. Boundaries are inclusive (30 candidates, 4 weeks, 0.95); a missing or non-finite figure is unavailable and never a pass; the result is derived from its five terms and nothing else.</summary>
public sealed class MlPromotionTests
{
    private static readonly MlInfluenceOptions Options = new();

    private static MlPromotionFacts Facts(int? candidates = 30, double? weeks = 4, double? probability = 0.95, CounterfactualOutcome? counterfactual = CounterfactualOutcome.NoDifference, string[]? rule = null) =>
        new(candidates, weeks, probability, counterfactual, rule ?? []);

    private static MlPromotionResult Evaluate(MlPromotionFacts facts) => MlPromotion.Evaluate(facts, Options);

    private static MlTermResult Term(MlPromotionResult r, string name) => r.Terms.Single(t => t.Name == name);

    [Fact]
    public void Exactly_at_every_bar_the_challenger_may_be_promoted()
    {
        var result = Evaluate(Facts());

        result.MayPromote.ShouldBeTrue();
        result.Reasons.ShouldBeEmpty();
        result.Terms.Select(t => t.Name).ShouldBe(MlPromotion.TermNames);
        result.Terms.ShouldAllBe(t => t.State == MlTermState.Holds);
    }

    [Theory]
    [InlineData(29, "need 30")] // the spec's hand case
    [InlineData(0, "need 30")]
    public void Fewer_than_30_graded_candidates_is_refused(int candidates, string text)
    {
        var result = Evaluate(Facts(candidates: candidates));

        result.MayPromote.ShouldBeFalse();
        Term(result, MlPromotion.EnoughCandidates).State.ShouldBe(MlTermState.Fails);
        result.Reasons.Single().Text.ShouldContain(text);
        result.Reasons.Single().Code.ShouldBe("promotion_too_few_candidates");
    }

    [Theory]
    [InlineData(3.0)] // the spec's hand case: 30 candidates and 3 weeks
    [InlineData(3.99)]
    public void Fewer_than_4_weeks_is_refused(double weeks)
    {
        var result = Evaluate(Facts(weeks: weeks));

        result.MayPromote.ShouldBeFalse();
        Term(result, MlPromotion.EnoughWeeks).State.ShouldBe(MlTermState.Fails);
        result.Reasons.Single().Text.ShouldContain("need 4 weeks");
    }

    [Theory]
    [InlineData(0.9499)]
    [InlineData(0.5)]
    public void A_probability_under_the_bar_of_the_paired_bootstrap_is_refused(double probability)
    {
        var result = Evaluate(Facts(probability: probability));

        result.MayPromote.ShouldBeFalse();
        Term(result, MlPromotion.BeatsChampion).State.ShouldBe(MlTermState.Fails);
        result.Reasons.Single().Code.ShouldBe("promotion_not_beating_champion");
    }

    [Theory]
    [InlineData(CounterfactualOutcome.NoDifference, true)]
    [InlineData(CounterfactualOutcome.RetrainingHelps, true)]
    [InlineData(CounterfactualOutcome.FrozenBetter, false)]
    [InlineData(CounterfactualOutcome.Inconclusive, false)]
    public void The_counterfactual_must_not_be_frozen_better_and_must_say_something(CounterfactualOutcome outcome, bool passes)
    {
        var result = Evaluate(Facts(counterfactual: outcome));

        result.MayPromote.ShouldBe(passes);
        Term(result, MlPromotion.CounterfactualNotFrozenBetter).Holds.ShouldBe(passes);
        if (outcome == CounterfactualOutcome.Inconclusive)
        {
            Term(result, MlPromotion.CounterfactualNotFrozenBetter).State.ShouldBe(MlTermState.Unavailable);
        }
    }

    [Fact]
    public void A_counterfactual_conclusion_the_rule_does_not_know_is_unavailable_never_a_pass()
    {
        var result = Evaluate(Facts(counterfactual: (CounterfactualOutcome)99));

        result.MayPromote.ShouldBeFalse();
        Term(result, MlPromotion.CounterfactualNotFrozenBetter).State.ShouldBe(MlTermState.Unavailable);
        result.Reasons.Single().Code.ShouldBe("promotion_counterfactual_unknown");
    }

    [Fact]
    public void A_reason_from_the_champion_rule_refuses_and_is_listed()
    {
        var result = Evaluate(Facts(rule: ["The verdict is not Eligible.", "No hold-out was kept."]));

        result.MayPromote.ShouldBeFalse();
        Term(result, MlPromotion.ChampionRuleMet).State.ShouldBe(MlTermState.Fails);
        result.Reasons.Select(r => r.Text).ShouldBe(["The verdict is not Eligible.", "No hold-out was kept."]);
    }

    [Fact]
    public void Every_unavailable_figure_is_never_a_pass_and_says_so()
    {
        var result = Evaluate(Facts(candidates: null, weeks: null, probability: null, counterfactual: null));

        result.MayPromote.ShouldBeFalse();
        new[] { MlPromotion.EnoughCandidates, MlPromotion.EnoughWeeks, MlPromotion.BeatsChampion, MlPromotion.CounterfactualNotFrozenBetter }.ShouldAllBe(n => Term(result, n).State == MlTermState.Unavailable);
        result.Reasons.Select(r => r.Code).ShouldBe(["promotion_candidates_unavailable", "promotion_weeks_unavailable", "promotion_probability_unavailable", "promotion_counterfactual_unavailable"]);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_figure_that_is_not_finite_is_unavailable_not_a_number_to_compare(double figure)
    {
        var weeks = Evaluate(Facts(weeks: figure));
        var probability = Evaluate(Facts(probability: figure));

        Term(weeks, MlPromotion.EnoughWeeks).State.ShouldBe(MlTermState.Unavailable);
        Term(probability, MlPromotion.BeatsChampion).State.ShouldBe(MlTermState.Unavailable);
        weeks.MayPromote.ShouldBeFalse();
        probability.MayPromote.ShouldBeFalse();
    }

    [Fact]
    public void Each_term_false_in_turn_gives_false_with_its_reason_and_the_others_still_hold()
    {
        var cases = new (MlPromotionFacts Facts, string Term)[]
        {
            (Facts(candidates: 1), MlPromotion.EnoughCandidates),
            (Facts(weeks: 1), MlPromotion.EnoughWeeks),
            (Facts(probability: 0.1), MlPromotion.BeatsChampion),
            (Facts(counterfactual: CounterfactualOutcome.FrozenBetter), MlPromotion.CounterfactualNotFrozenBetter),
            (Facts(rule: ["x"]), MlPromotion.ChampionRuleMet),
        };

        foreach (var (facts, term) in cases)
        {
            var result = Evaluate(facts);

            result.MayPromote.ShouldBeFalse(term);
            result.Terms.Where(t => !t.Holds).Select(t => t.Name).ShouldBe([term]);
        }
    }

    [Fact]
    public void The_result_is_true_only_for_exactly_the_five_terms_in_order()
    {
        var holds = Enumerable.Range(0, 5).Select(i => new MlTermResult(MlPromotion.TermNames[i], MlTermState.Holds, [])).ToList();

        new MlPromotionResult(holds).MayPromote.ShouldBeTrue();
        new MlPromotionResult([.. holds.Take(4)]).MayPromote.ShouldBeFalse("a missing term is not a pass");
        new MlPromotionResult([.. holds.AsEnumerable().Reverse()]).MayPromote.ShouldBeFalse("another order is not the rule");
        new MlPromotionResult([]).MayPromote.ShouldBeFalse();
        new MlPromotionResult([.. holds.Take(4), new MlTermResult("Other", MlTermState.Holds, [])]).MayPromote.ShouldBeFalse();
        default(MlTermState).ShouldBe(MlTermState.Unavailable);
    }

    [Fact]
    public void The_bars_follow_the_settings_which_can_only_be_tightened()
    {
        var strict = new MlInfluenceOptions(ForwardMinCandidates: 60, ForwardMinWeeks: 8, HoldOutMinProbability: 0.99);

        MlPromotion.Evaluate(Facts(candidates: 59), strict).MayPromote.ShouldBeFalse();
        MlPromotion.Evaluate(Facts(candidates: 60, weeks: 7.9, probability: 0.99), strict).MayPromote.ShouldBeFalse();
        MlPromotion.Evaluate(Facts(candidates: 60, weeks: 8, probability: 0.98), strict).MayPromote.ShouldBeFalse();
        MlPromotion.Evaluate(Facts(candidates: 60, weeks: 8, probability: 0.99), strict).MayPromote.ShouldBeTrue();
    }
}
