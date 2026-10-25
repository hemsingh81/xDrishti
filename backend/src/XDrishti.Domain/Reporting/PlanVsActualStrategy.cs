namespace XDrishti.Domain.Reporting;

/// <summary>Plan versus actual of one strategy (P8-04): means over its graded tickets, each figure insufficient below the minimum.</summary>
/// <param name="StrategyId">The strategy.</param>
/// <param name="N">Graded tickets.</param>
/// <param name="PlanR">Mean plan R.</param>
/// <param name="ActualR">Mean realised R after charges.</param>
/// <param name="EntrySlippageR">Mean entry slippage in R (positive is worse).</param>
public sealed record PlanVsActualStrategy(string StrategyId, int N, Metric PlanR, Metric ActualR, Metric EntrySlippageR);
