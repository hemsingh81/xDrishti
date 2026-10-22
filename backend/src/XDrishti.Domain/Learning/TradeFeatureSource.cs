namespace XDrishti.Domain.Learning;

/// <summary>Where a decision-time feature is computed.</summary>
public enum TradeFeatureSource
{
    /// <summary>From the 1-minute bars closed before the decision instant (price, volume, session fields) and the date, by the feature frame.</summary>
    Bars,

    /// <summary>From the daily market context of the previous session (P3-01d); unknown while that context is not loaded.</summary>
    Context,

    /// <summary>From the trade's own plan at the decision: side, time of day, stop distance, planned reward to risk.</summary>
    Plan,

    /// <summary>From the cell statistics as of the day before the decision date (P6-01), computed when the training set is built.</summary>
    Cell,
}
