namespace XDrishti.Domain.Selection;

/// <summary>An open trade or holding the new tickets must not duplicate.</summary>
/// <param name="Symbol">The instrument.</param>
/// <param name="Side">BUY or SELL.</param>
public sealed record OpenPosition(string Symbol, string Side);
