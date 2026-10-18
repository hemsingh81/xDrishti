namespace XDrishti.Domain.Research;

/// <summary>What the fill model does not do (P3-03b "Limits that stay"). The run banner lists them.</summary>
public static class FillLimits
{
    public static IReadOnlyList<FillLimit> NotModelled { get; } =
    [
        new("auction_open", "The first 1-minute open of the data can be the auction price. A position held overnight that gaps through a stop or a target is filled in that minute (a stop at the open, a target at its level), and a repaired opening bar has a wider range, so its slippage is larger, never smaller."),
        new("circuit_locks", "Locked circuits are not modelled: there is no price-band data, so a fill at a locked price is not refused."),
        new("touch_is_fill", "A resting limit fills when the price touches it; real queues fill less often, and paying the base slippage on targets would take about 0.1 R off every winner."),
        new("no_queue_or_impact", "There is no queue position, partial fill, market impact of the order size or liquidity cap."),
        new("grid_approximate", "The tick is today's tick from the instrument master; old prices can lie on a finer grid or off the grid, so rounding to the tick is approximate."),
        new("minute_path", "Inside one minute only the open, high, low and close are known; a minute that reaches both stop and target is taken as the stop."),
        new("hole_is_no_trade", "An absent minute and a minute with no volume are read as a minute without a trade; the order rests through it, up to the 5-minute bound."),
        new("one_minute_entry", "The entry order lives for one minute; a miss is never retried."),
        new("no_latency", "The order is live at the instant the trigger bar closes and fills at the first print of the next minute; decision and network latency are zero."),
        new("dividends_not_modelled", "Cash dividends are neither adjusted in the prices nor received: a position held over an ex-date shows the price drop without the dividend."),
    ];
}
