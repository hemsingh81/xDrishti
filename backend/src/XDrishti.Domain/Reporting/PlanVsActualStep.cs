namespace XDrishti.Domain.Reporting;

/// <summary>One step of the chain from the plan's R to the realised R (P8-04), a mean over the same tickets.</summary>
/// <param name="Step">plan_prices, entry_slippage, charges, not_attributed or actual.</param>
/// <param name="Label">The step in words.</param>
/// <param name="Metric">The mean in R (a cost is negative); a non-value carries no number.</param>
/// <param name="N">Tickets behind it.</param>
public sealed record PlanVsActualStep(string Step, string Label, Metric Metric, int N);
