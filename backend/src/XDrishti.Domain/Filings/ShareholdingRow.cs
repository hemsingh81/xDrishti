namespace XDrishti.Domain.Filings;

/// <summary>One accepted line of a shareholding csv: <see cref="Pct"/> and <see cref="PledgedPct"/> are null when the cell was blank (unavailable, never zero).</summary>
public sealed record ShareholdingRow(int Line, string Symbol, DateOnly PeriodEnd, string Category, decimal? Pct, decimal? PledgedPct, BroadcastTime Broadcast);
