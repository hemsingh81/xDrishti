using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>Reads the chosen price of a bar.</summary>
internal static class PriceSources
{
    public static double Pick(PriceSource source, in Bar bar) => source switch
    {
        PriceSource.Open => bar.Open,
        PriceSource.High => bar.High,
        PriceSource.Low => bar.Low,
        _ => bar.Close,
    };
}
