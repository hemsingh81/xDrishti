using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>Reads the chosen price of a bar.</summary>
internal static class PriceSources
{
    /// <summary>The chosen price; <see cref="PriceSource.Default"/> reads the close (the catalog has resolved it to the indicator's own default before an indicator is built).</summary>
    public static double Pick(PriceSource source, in Bar bar) => source switch
    {
        PriceSource.Open => bar.Open,
        PriceSource.High => bar.High,
        PriceSource.Low => bar.Low,
        PriceSource.Volume => bar.Volume,
        _ => bar.Close,
    };
}
