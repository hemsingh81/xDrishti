namespace XDrishti.Domain.Learning;

/// <summary>The counts of <see cref="SmartRulesEvidence"/>.</summary>
/// <param name="PlansRead">Plans counted.</param>
/// <param name="From">Earliest planned session; null without a plan.</param>
/// <param name="To">Latest planned session; null without a plan.</param>
/// <param name="Rules">The five rules in a fixed order.</param>
/// <param name="Flagged">Plans flagged for review, oldest first.</param>
public sealed record SmartRulesCounts(int PlansRead, DateOnly? From, DateOnly? To, IReadOnlyList<RuleCounts> Rules, IReadOnlyList<FlaggedPlan> Flagged);
