namespace XDrishti.Application.Abstractions.Pairs;

/// <summary>A row of <c>research.pair_trades</c>.</summary>
public sealed record PairTradeRow(
    string RunId, string PairKey, int Ordinal, DateTimeOffset EntryTs, DateTimeOffset ExitTs, string Side, double ZEntry, double? ZExit, string ExitReason, int QtyY, int QtyX, decimal GrossInr, decimal? NetInr, double R, IReadOnlyList<string> Labels);
