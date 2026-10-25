namespace XDrishti.Domain.Research;

/// <summary>The replay of an instrument and the explanation of each requested trade whose decision it made; a request that matches no candidate has no entry (P3-04d).</summary>
public sealed record ExplainResult(InstrumentReplay Replay, IReadOnlyDictionary<string, TradeExplanation> ByTradeKey);
