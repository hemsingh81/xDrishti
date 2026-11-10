namespace XDrishti.Domain.Reporting;

/// <summary>One instrument-day left out of the index, named and counted, never repaired (ADR 0102).</summary>
/// <param name="Symbol">The member.</param>
/// <param name="Date">The day of the move.</param>
/// <param name="Reason">Why (<see cref="BuyAndHold.Discontinuity"/>).</param>
public sealed record LeftOutDay(string Symbol, DateOnly Date, string Reason);
