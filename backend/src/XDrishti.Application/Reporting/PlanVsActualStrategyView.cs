namespace XDrishti.Application.Reporting;

/// <summary>Plan versus actual of one strategy (P8-04).</summary>
/// <param name="StrategyId">The strategy.</param>
/// <param name="N">Graded tickets.</param>
/// <param name="PlanR">Mean plan R.</param>
/// <param name="ActualR">Mean realised R after charges.</param>
/// <param name="EntrySlippageR">Mean entry slippage in R.</param>
public sealed record PlanVsActualStrategyView(string StrategyId, int N, MetricView PlanR, MetricView ActualR, MetricView EntrySlippageR);
