namespace XDrishti.Domain.Options.Costs;

/// <summary>
/// One strike and side of a recorded chain as the half-spread model sees it (P14-07): the instant, the expiry, the strike and the underlying's price at the snapshot (the bucket keys, paise), and the bid and the ask (paise; null when Dhan sent
/// none). Nothing else of the chain row is read: no IV, no Greek, no open interest, no volume.
/// </summary>
public sealed record SpreadQuote(DateTimeOffset Ts, DateOnly Expiry, int StrikePx, int UnderlyingPx, int? BidPx, int? AskPx)
{
    /// <summary>A bid and an ask, both above zero, the bid strictly below the ask. A crossed or locked (bid equal to ask) quote is never counted: a locked one would read as a zero spread.</summary>
    public bool TwoSided => BidPx is > 0 && AskPx is > 0 && BidPx < AskPx;
}
