namespace XDrishti.Domain.Research;

/// <summary>What the capacity rule of CF-05 (D4) said of one order. An order over a cap that can be judged is a breach even when the other cap cannot be read; with no such breach, a missing traded value is a reading with an unavailable reason (<see cref="Breach"/> true), never a pass.</summary>
/// <param name="Breach">True when the order is over either cap or a figure to judge it by is missing (a reason starting with <c>unavailable</c> says the latter).</param>
/// <param name="MinuteCap">The most the order may be: the participation share of the fill minute's traded value; null without one.</param>
/// <param name="DailyCap">The most the order may be: the participation share of the 20-day median daily value; null without one.</param>
/// <param name="Reason">Which cap was breached, or why it could not be judged; empty when the order is within both.</param>
public sealed record CapacityCheck(bool Breach, decimal? MinuteCap, decimal? DailyCap, string Reason);
