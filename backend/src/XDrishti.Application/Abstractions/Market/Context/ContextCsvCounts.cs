namespace XDrishti.Application.Abstractions.Market.Context;

/// <summary>The days that came from dropped CSVs: index days stored with source <c>nse_csv</c>, VIX closes of the CSV side table, and the newest and oldest index day among them.</summary>
public sealed record ContextCsvCounts(int IndexDays, int VixDays, DateOnly? First, DateOnly? Last);
