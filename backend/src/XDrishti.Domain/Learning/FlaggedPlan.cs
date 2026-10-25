namespace XDrishti.Domain.Learning;

/// <summary>A plan that carries <c>rule_removed_half</c> (doc 09 section 14). The rule that removed the candidates is only in the text of the banner, which the plan does not store, so it is not named here.</summary>
/// <param name="PlanId">The plan.</param>
/// <param name="ForDate">The planned session.</param>
/// <param name="Code">The flag code.</param>
public sealed record FlaggedPlan(long PlanId, DateOnly ForDate, string Code);
