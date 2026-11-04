namespace XDrishti.Domain.Research;

/// <summary>What the capacity rule of CF-05 (D4) said of one order. A missing traded value is a breach with an unavailable reason, never a pass.</summary>
/// <param name="Breach">True when the order is over either cap or a figure to judge it by is missing.</param>
/// <param name="MinuteCap">The most the order may be: the participation share of the fill minute's traded value; null without one.</param>
/// <param name="DailyCap">The most the order may be: the participation share of the 20-day median daily value; null without one.</param>
/// <param name="Reason">Which cap was breached, or why it could not be judged; empty when the order is within both.</param>
public sealed record CapacityCheck(bool Breach, decimal? MinuteCap, decimal? DailyCap, string Reason);
