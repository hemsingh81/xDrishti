namespace XDrishti.Domain.Research;

/// <summary>
/// Why an entry candidate did not become a trade (ADR 0037, decision D3; ADR 0038). Counted, never silent. It lives here, and not with the walker, because the run record of P3-04 holds it and a Domain type cannot name a type of
/// <c>XDrishti.Simulation</c>. Stored by name, never renamed.
/// </summary>
public enum NoTradeReason
{
    /// <summary>There is no minute to fill in: none that holds a trade follows the decision, it is on another India date, or it starts more than the allowed delay after the decision instant.</summary>
    NoFillBar,

    /// <summary>The first minute that holds a trade after the decision starts at or after the square-off minute.</summary>
    AfterSquareOff,

    /// <summary>The fill price is at or through the stop, so the trade would have been stopped out before it began.</summary>
    EntryThroughStop,

    /// <summary>The entry limit order was not reached in the one minute it lives (ADR 0038, E5): the minute opened beyond the limit and did not trade back to it.</summary>
    LimitNotReached,

    /// <summary>The instrument already had an open position on this side (ADR 0040, D11): the candidate was not walked. Produced by the simulator's position gate, never by the walker.</summary>
    PositionOpen,

    /// <summary>
    /// The order was placed for the next session (ADR 0043, E8) and the list of minutes ends at the candidate's own minute, so the fill session is not in the data: the order may be filled by a longer list, and no miss can be said of it. A no-trade of its own,
    /// never <see cref="NoFillBar"/>, so that the last day of a window does not depend on how far the read goes.
    /// </summary>
    OrderPendingAtEnd,

    /// <summary>
    /// The filter <c>not_event_day</c> blocked the entry: the next results event session is 0 to H sessions away at the decision instant, H being the side's longest hold (P15-06, ADR 0182). Produced by the simulator before the walk, never by the walker; the candidate
    /// does not open a position, so the position gate is not occupied by it.
    /// </summary>
    EventBlocked,

    /// <summary>
    /// The filter <c>not_event_day</c> found no visible results entry for the instrument at the decision instant (P15-06): nothing is known, so there is no entry. Never a pass.
    /// </summary>
    EventUnavailable,

    /// <summary>
    /// The run asked for conservative tiered slippage (CF-05, D2, ADR 0229) and the instrument is tier C (not priced) or its tier is unavailable at the entry date: the trade is excluded and counted, never priced as tier A. Produced by the walker before the fill is priced.
    /// </summary>
    TierExcluded,

    /// <summary>
    /// The filter <c>min_turnover</c> blocked the entry (CF-05, ADR 0230): the 20-day median traded value before the entry date is under the threshold, or it cannot be known (missing bars, under 20 sessions, missing volume). Never a pass. Produced by the walker.
    /// </summary>
    TurnoverBlocked,
}
