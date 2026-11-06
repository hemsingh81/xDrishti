using System.Globalization;
using XDrishti.Domain.Confidence;

namespace XDrishti.Domain.Learning;

/// <summary>What is known about a challenger when it is considered for promotion (CF-10 part 10d-1, spec D7). A part that is null or not finite is not available, and a term with such a figure never holds.</summary>
/// <param name="GradedCandidates">Graded shadow candidates scored by the challenger.</param>
/// <param name="Weeks">Weeks they span.</param>
/// <param name="ProbabilityBeatsChampion">The probability, by the paired day-cluster bootstrap on the same candidates (with the take rule of P6-13), that the challenger beats the champion.</param>
/// <param name="Counterfactual">The P6-12 counterfactual's conclusion for the challenger's recipe; null when none is stored for it.</param>
/// <param name="ChampionRuleReasons">What <see cref="ChampionRule"/> says against the model; empty when it may be champion.</param>
public sealed record MlPromotionFacts(int? GradedCandidates, double? Weeks, double? ProbabilityBeatsChampion, CounterfactualOutcome? Counterfactual, IReadOnlyList<string> ChampionRuleReasons);

/// <summary>The answer of the promotion rule: the five terms and, derived from them and from nothing else, whether the challenger may be promoted (CF-10 part 10d-1).</summary>
/// <param name="Terms">The terms in the order of <see cref="MlPromotion.TermNames"/>.</param>
public sealed record MlPromotionResult(IReadOnlyList<MlTermResult> Terms)
{
    /// <summary>True only when the terms are exactly the five of <see cref="MlPromotion.TermNames"/>, in that order, and every one holds. Any other list is false.</summary>
    public bool MayPromote => Terms.Select(t => t.Name).SequenceEqual(MlPromotion.TermNames, StringComparer.Ordinal) && Terms.All(t => t.Holds);

    /// <summary>Every reason of every term that does not hold, in term order; empty only when <see cref="MayPromote"/>.</summary>
    public IReadOnlyList<MlInfluenceReason> Reasons => [.. Terms.SelectMany(t => t.Reasons)];
}

/// <summary>
/// The rule for promoting a challenger to champion (CF-10 part 10d-1, spec D7, ADR 0220). Pure and deterministic. A challenger may be promoted only when it has at least 30 graded candidates over at least 4 weeks, the paired day-cluster bootstrap against the champion on the same
/// candidates gives <c>P &gt;= 0.95</c>, the P6-12 counterfactual for its recipe is not <see cref="CounterfactualOutcome.FrozenBetter"/> (and says something: an inconclusive one is not a pass), and <see cref="ChampionRule"/> has nothing against it. A missing, null or non-finite figure is
/// unavailable and never a pass. The bars are those of <see cref="MlInfluenceOptions"/> (the same 30, 4 and 0.95 as the forward and hold-out terms of the influence predicate).
/// </summary>
public static class MlPromotion
{
    /// <summary>Enough graded candidates.</summary>
    public const string EnoughCandidates = nameof(EnoughCandidates);

    /// <summary>Enough weeks of them.</summary>
    public const string EnoughWeeks = nameof(EnoughWeeks);

    /// <summary>The paired bootstrap against the champion.</summary>
    public const string BeatsChampion = nameof(BeatsChampion);

    /// <summary>The P6-12 counterfactual is not FrozenBetter.</summary>
    public const string CounterfactualNotFrozenBetter = nameof(CounterfactualNotFrozenBetter);

    /// <summary>The champion rule has nothing against the model.</summary>
    public const string ChampionRuleMet = nameof(ChampionRuleMet);

    /// <summary>The five terms, in the order a result lists them.</summary>
    public static IReadOnlyList<string> TermNames { get; } = [EnoughCandidates, EnoughWeeks, BeatsChampion, CounterfactualNotFrozenBetter, ChampionRuleMet];

    /// <summary>Evaluates the rule.</summary>
    public static MlPromotionResult Evaluate(MlPromotionFacts facts, MlInfluenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(options);
        var c = CultureInfo.InvariantCulture;
        return new MlPromotionResult(
        [
            Term(EnoughCandidates, facts.GradedCandidates is { } n ? n >= options.ForwardMinCandidates : null, "promotion_too_few_candidates", string.Create(c, $"{facts.GradedCandidates} graded candidates; need {options.ForwardMinCandidates}."), "promotion_candidates_unavailable", "The number of graded candidates is not available."),
            Term(EnoughWeeks, Finite(facts.Weeks) is { } w ? w >= options.ForwardMinWeeks : null, "promotion_too_short", string.Create(c, $"The candidates span {facts.Weeks:F1} weeks; need {options.ForwardMinWeeks} weeks."), "promotion_weeks_unavailable", "The weeks the candidates span are not available."),
            Term(BeatsChampion, Finite(facts.ProbabilityBeatsChampion) is { } p ? p >= options.HoldOutMinProbability : null, "promotion_not_beating_champion", string.Create(c, $"The probability of beating the champion on the same candidates is {facts.ProbabilityBeatsChampion:F4}; at least {options.HoldOutMinProbability:F2} is needed."), "promotion_probability_unavailable", "The paired comparison with the champion is not available."),
            Counterfactual(facts.Counterfactual),
            ChampionRule(facts.ChampionRuleReasons),
        ]);
    }

    private static MlTermResult Counterfactual(CounterfactualOutcome? outcome) => outcome switch
    {
        null => Unavailable(CounterfactualNotFrozenBetter, "promotion_counterfactual_unavailable", "No P6-12 counterfactual is stored for the challenger's recipe."),
        CounterfactualOutcome.Inconclusive => Unavailable(CounterfactualNotFrozenBetter, "promotion_counterfactual_inconclusive", "The P6-12 counterfactual for the challenger's recipe is inconclusive; that is not a pass."),
        CounterfactualOutcome.FrozenBetter => new MlTermResult(CounterfactualNotFrozenBetter, MlTermState.Fails, [new MlInfluenceReason("promotion_frozen_better", "The P6-12 counterfactual says the frozen model scored better than retraining for this recipe.")]),
        CounterfactualOutcome.NoDifference or CounterfactualOutcome.RetrainingHelps => new MlTermResult(CounterfactualNotFrozenBetter, MlTermState.Holds, []),
        _ => Unavailable(CounterfactualNotFrozenBetter, "promotion_counterfactual_unknown", "The P6-12 counterfactual for the challenger's recipe has a conclusion this rule does not know; that is not a pass."), // fail closed: a value added to the enum later is never a pass until it is listed above
    };

    private static MlTermResult ChampionRule(IReadOnlyList<string>? reasons) =>
        reasons is null
            ? Unavailable(ChampionRuleMet, "promotion_champion_rule_unavailable", "The champion rule could not be read.")
            : reasons.Count == 0
                ? new MlTermResult(ChampionRuleMet, MlTermState.Holds, [])
                : new MlTermResult(ChampionRuleMet, MlTermState.Fails, [.. reasons.Select(r => new MlInfluenceReason("promotion_champion_rule", r))]);

    private static MlTermResult Term(string name, bool? passes, string failCode, string failText, string missingCode, string missingText) => passes switch
    {
        true => new MlTermResult(name, MlTermState.Holds, []),
        false => new MlTermResult(name, MlTermState.Fails, [new MlInfluenceReason(failCode, failText)]),
        null => Unavailable(name, missingCode, missingText),
    };

    private static MlTermResult Unavailable(string name, string code, string text) => new(name, MlTermState.Unavailable, [new MlInfluenceReason(code, text)]);

    private static double? Finite(double? value) => value is { } v && double.IsFinite(v) ? v : null;
}
