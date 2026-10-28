using XDrishti.Domain.Reporting;

namespace XDrishti.Domain.LiveTrading;

/// <summary>The capacity score of one scope: the share of orders filled in full as a <see cref="Metric"/> (a non-value has no number) with its 95 % credible interval, present only for a value.</summary>
public sealed record CapacityResult(Metric Score, double? Lo, double? Hi, int PriorStrength);
