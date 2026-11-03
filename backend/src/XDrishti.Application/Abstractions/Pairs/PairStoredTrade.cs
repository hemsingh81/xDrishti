namespace XDrishti.Application.Abstractions.Pairs;

/// <summary>A stored pair trade as the evaluation reads it.</summary>
public sealed record PairStoredTrade(string RunId, string PairKey, DateOnly Date, long EntryEpochSeconds, double R, decimal GrossInr, decimal? NetInr, IReadOnlyList<string> Labels);
