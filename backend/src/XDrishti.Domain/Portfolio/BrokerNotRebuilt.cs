namespace XDrishti.Domain.Portfolio;

/// <summary>A holding (or security) the automatic rebuild after a trade sync left alone, and why: <c>needs_review</c>, <c>quantity_mismatch</c>, <c>ambiguous</c>, <c>no_holding</c> or <c>changed_meanwhile</c>. The symbol is a ticker, never trade content.</summary>
public sealed record BrokerNotRebuilt(string Symbol, string Reason);
