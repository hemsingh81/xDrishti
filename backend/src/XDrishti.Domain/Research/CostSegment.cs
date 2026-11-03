namespace XDrishti.Domain.Research;

/// <summary>Which rates of a charge schedule a trade pays (ADR 0042, D12). Stored by name, never renamed.</summary>
public enum CostSegment : byte
{
    /// <summary>The trade is opened and closed within the day: intraday cash-market equity.</summary>
    IntradayEquity = 0,

    /// <summary>The trade is a swing position that may be held overnight: delivery equity.</summary>
    Delivery = 1,

    /// <summary>An option leg (P14-07): the turnover is premium times units. Its rates are optional on a sheet; without them the leg has no costs (never zero).</summary>
    Options = 2,

    /// <summary>A futures leg (P14-07): the turnover is price times units. Its rates are optional on a sheet; without them the leg has no costs (never zero).</summary>
    Futures = 3,
}
