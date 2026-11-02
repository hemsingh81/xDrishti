namespace XDrishti.Domain.Filings;

/// <summary>The current visible shareholding group of an instrument at an instant (<see cref="PeriodEnd"/> and the time it became public).</summary>
public sealed record ShareholdingSnapshot(DateOnly PeriodEnd, DateTimeOffset BroadcastTs, IReadOnlyList<ShareholdingLine> Lines, bool SumOff);
