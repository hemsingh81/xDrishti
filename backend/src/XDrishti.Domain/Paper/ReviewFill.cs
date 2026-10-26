namespace XDrishti.Domain.Paper;

/// <summary>A real broker fill with no ticket (a discretionary trade) from the journal (spec P10-03). Counted, never graded in R.</summary>
/// <param name="AccountId">The account.</param>
/// <param name="Symbol">The symbol.</param>
/// <param name="IsBuy">Whether it is a buy.</param>
/// <param name="Qty">Shares.</param>
/// <param name="Price">The price.</param>
/// <param name="Charges">The recorded charges.</param>
public sealed record ReviewFill(Guid AccountId, string Symbol, bool IsBuy, long Qty, decimal Price, decimal Charges);
