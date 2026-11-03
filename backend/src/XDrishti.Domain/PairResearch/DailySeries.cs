namespace XDrishti.Domain.PairResearch;

/// <summary>The daily points an instrument has, in any order and possibly beyond the quarter: <see cref="PairWindow"/> uses only those dated before the quarter starts.</summary>
public sealed record DailySeries(string Symbol, string? Sector, IReadOnlyList<DailyPoint> Points);
