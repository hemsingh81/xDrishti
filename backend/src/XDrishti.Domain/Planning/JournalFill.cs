using XDrishti.Domain.Research;

namespace XDrishti.Domain.Planning;

/// <summary>
/// One fill the existing read-only broker sync stored (<c>portfolio.transactions</c>, source broker, not voided), as the journal sees it (P7-14, ADR 0093). Plain data: nothing here can reach a broker.
/// </summary>
/// <param name="TransactionId">The stored trade.</param>
/// <param name="AccountId">The broker account the trade belongs to.</param>
/// <param name="Symbol">The normalised trading symbol.</param>
/// <param name="Side">Buy or sell.</param>
/// <param name="Date">The India date of the trade.</param>
/// <param name="Time">The India time of the trade; null when the source has none.</param>
/// <param name="Qty">Shares.</param>
/// <param name="Price">Price per share, rupees.</param>
/// <param name="Charges">The charges of the trade as one total, rupees.</param>
public sealed record JournalFill(Guid TransactionId, Guid AccountId, string Symbol, Side Side, DateOnly Date, TimeOnly? Time, long Qty, decimal Price, decimal Charges);
