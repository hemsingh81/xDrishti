namespace XDrishti.Application.Reporting;

/// <summary>One step of the chain (P8-04).</summary>
/// <param name="Step">plan_prices, entry_slippage, charges, not_attributed or actual.</param>
/// <param name="Label">The step in words.</param>
/// <param name="Metric">Mean in R.</param>
/// <param name="N">Tickets behind it.</param>
public sealed record PlanVsActualStepView(string Step, string Label, MetricView Metric, int N);
