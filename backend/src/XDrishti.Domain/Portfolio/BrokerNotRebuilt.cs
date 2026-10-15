namespace XDrishti.Domain.Portfolio;

/// <summary>
/// A holding (or security) the automatic rebuild after a trade sync left alone, and why: <c>needs_review</c>, <c>quantity_mismatch</c>, <c>ambiguous</c>, <c>no_holding</c> or <c>changed_meanwhile</c>. The symbol is a ticker, never trade content.
/// <c>Detail</c> is the kind of mismatch for <c>quantity_mismatch</c> (<c>negative_opening</c>, <c>sell_exceeds_open</c>, <c>fractional_after_adjustment</c>: the first two are what an earlier read of the history can fix) and
/// <c>HoldingId</c> the single holding it is about (null for a security without one), so that a notice can open it.
/// </summary>
public sealed record BrokerNotRebuilt(string Symbol, string Reason, string? Detail = null, Guid? HoldingId = null);
