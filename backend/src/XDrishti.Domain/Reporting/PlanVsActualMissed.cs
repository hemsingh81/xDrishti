namespace XDrishti.Domain.Reporting;

/// <summary>Triggered tickets that had no fill (P8-04): outside the chain, a count and the plan R they would have returned.</summary>
/// <param name="Count">Approved lines without a fill whose plan-price grade triggered.</param>
/// <param name="TotalPlanR">Their plan R summed; a non-value when there is none.</param>
public sealed record PlanVsActualMissed(int Count, Metric TotalPlanR);
