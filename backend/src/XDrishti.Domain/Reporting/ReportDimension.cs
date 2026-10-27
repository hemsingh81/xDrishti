namespace XDrishti.Domain.Reporting;

/// <summary>The dimensions a report can group by (the lenses of doc 13 and P8-00 D4). <see cref="Regime"/> and <see cref="Account"/> are named so a request for them gets a typed refusal with the reason, not a silent empty answer.</summary>
public enum ReportDimension
{
    /// <summary>The baskets the instrument belonged to in the run; a trade in several baskets appears under each (not additive).</summary>
    Basket,

    /// <summary>The instrument (its symbol).</summary>
    Instrument,

    /// <summary>The strategy id.</summary>
    Strategy,

    /// <summary>The evaluation timeframe code.</summary>
    Timeframe,

    /// <summary>The side of the trade.</summary>
    Side,

    /// <summary>The trading style, a label of the run.</summary>
    Style,

    /// <summary>The exit policy, a label of the run.</summary>
    Exit,

    /// <summary>The month of the entry, <c>yyyy-MM</c>.</summary>
    Month,

    /// <summary>The weekday of the entry.</summary>
    Weekday,

    /// <summary>Market regime: unavailable in the report engine (the regime of a trade is read for the regime cells and the Lab, P6-02).</summary>
    Regime,

    /// <summary>Account: unavailable, a simulated trade has no account.</summary>
    Account,

    /// <summary>The sector of the instrument as of the read (the master keeps one current sector); not recorded when the master has none.</summary>
    Sector,

    /// <summary>The India-time hour of the entry (09 to 15), for intraday runs only; a swing or unlabelled run has no hour.</summary>
    Hour,
}
