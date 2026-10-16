using XDrishti.Domain.Market.Bars;
using XDrishti.Indicators;

namespace XDrishti.Strategies.Tests.Support;

/// <summary>Expected feature specs, written out by hand in the canonical form (the source resolved: the indicator's own default, or close for an indicator that reads the whole bar).</summary>
internal static class Specs
{
    public static FeatureSpec Of(string name, Timeframe timeframe, double p1 = 0, double p2 = 0, double p3 = 0, PriceSource source = PriceSource.Close) =>
        new(name, timeframe, source, p1, p2, p3);

    public static FeatureSpec Close(Timeframe timeframe) => Of("close", timeframe);

    public static FeatureSpec Rsi(Timeframe timeframe, double n = 14, PriceSource source = PriceSource.Close) => Of("rsi", timeframe, n, source: source);

    public static FeatureSpec Ema(Timeframe timeframe, double n = 20, PriceSource source = PriceSource.Close) => Of("ema", timeframe, n, source: source);
}
