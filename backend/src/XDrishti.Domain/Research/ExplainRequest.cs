namespace XDrishti.Domain.Research;

/// <summary>A trade to explain: its key and the decision it was entered on (P3-04d).</summary>
public sealed record ExplainRequest(string TradeKey, long DecisionEpochSeconds, Side Side);
