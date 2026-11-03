namespace XDrishti.Domain.PairResearch;

/// <summary>One stored pair trade as protocol S reads it: the entry date, the pair, R, the gross and net rupees (net null: gross) and the turnover.</summary>
public sealed record PairTradeSample(DateOnly Date, long EntryEpochSeconds, string PairKey, double R, decimal GrossInr, decimal? NetInr, decimal TurnoverInr);
