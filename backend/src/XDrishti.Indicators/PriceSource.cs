namespace XDrishti.Indicators;

/// <summary>Which price of a bar an indicator reads (indicators that need the whole bar, such as <c>atr</c> or <c>vwap</c>, ignore it).</summary>
public enum PriceSource
{
    Close,
    Open,
    High,
    Low,
}
