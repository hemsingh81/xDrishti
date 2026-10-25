namespace XDrishti.Domain.Learning;

/// <summary>One stored plan as the smart-rule evidence reads it (P8-13).</summary>
/// <param name="PlanId">The plan id.</param>
/// <param name="ForDate">The planned session.</param>
/// <param name="Items">Its items.</param>
public sealed record PlanRuleFacts(long PlanId, DateOnly ForDate, IReadOnlyList<PlanItemFacts> Items);
