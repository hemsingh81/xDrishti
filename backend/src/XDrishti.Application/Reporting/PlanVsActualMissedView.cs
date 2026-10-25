namespace XDrishti.Application.Reporting;

/// <summary>Triggered tickets without a fill (P8-04).</summary>
/// <param name="Count">Tickets.</param>
/// <param name="TotalPlanR">Their plan R summed.</param>
public sealed record PlanVsActualMissedView(int Count, MetricView TotalPlanR);
