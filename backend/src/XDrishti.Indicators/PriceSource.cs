namespace XDrishti.Indicators;

/// <summary>Which price of a bar an indicator reads (indicators that need the whole bar, such as <c>atr</c> or <c>vwap</c>, ignore it).</summary>
public enum PriceSource
{
    /// <summary>
    /// The zero value, so that a <see cref="FeatureSpec"/> that names no source means "the indicator's own default": the catalog resolves it when the frame is created (high for <c>highest</c>, low for
    /// <c>lowest</c>, close otherwise, <see cref="IndicatorInfo.DefaultSource"/>). A spec can never keep it as a feature of its own: it is the same feature as the explicit default source.
    /// </summary>
    Default = 0,
    Close,
    Open,
    High,
    Low,

    /// <summary>The traded volume. Only the raw bar feature <c>volume</c> reads it (<see cref="RawBar"/>); it is not a price a spec may choose for another indicator.</summary>
    Volume,
}
