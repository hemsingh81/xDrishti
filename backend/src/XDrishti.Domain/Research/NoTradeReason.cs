namespace XDrishti.Domain.Research;

/// <summary>
/// Why an entry candidate did not become a trade (ADR 0037, decision D3). Counted, never silent. It lives here, and not with the walker, because the run record of P3-04 holds it and a Domain type cannot name a type of
/// <c>XDrishti.Simulation</c>. Stored by name, never renamed. P3-03b adds <c>LimitNotReached</c>.
/// </summary>
public enum NoTradeReason
{
    /// <summary>There is no minute to fill in: none follows the decision, it is on another India date, or it starts more than the allowed delay after the decision instant.</summary>
    NoFillBar,

    /// <summary>The first minute after the decision starts at or after the square-off minute.</summary>
    AfterSquareOff,

    /// <summary>The fill price is at or through the stop, so the trade would have been stopped out before it began.</summary>
    EntryThroughStop,
}
