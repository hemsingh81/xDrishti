namespace XDrishti.Domain.Market.Context;

/// <summary>One accepted day of a dropped CSV. High and Low are always set for an index file and may be null for a VIX file.</summary>
public sealed record ContextCsvRow(DateOnly Date, double? High, double? Low, double Close, int Line);
