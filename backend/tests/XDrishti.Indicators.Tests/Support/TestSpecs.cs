using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators.Tests.Support;

/// <summary>Feature specs that exercise every catalog entry with more than one parameter set (the edge ones included).</summary>
internal static class TestSpecs
{
    /// <summary>Several specs per catalog indicator on the timeframe; indicators that cannot run there (vwap, opening range on 1D) are left out.</summary>
    public static List<FeatureSpec> Variants(Timeframe timeframe)
    {
        var length = Math.Max(1, timeframe.Minutes());
        var specs = new List<FeatureSpec>();
        foreach (var info in IndicatorCatalog.All)
        {
            if (info.IntradayOnly && timeframe == Timeframe.D1)
            {
                continue;
            }

            if (info.Parameters.Count == 0)
            {
                specs.Add(info.DefaultSpec(timeframe));
                continue;
            }

            var values = info.Name.StartsWith("or_", StringComparison.Ordinal)
                ? new[] { length, 2 * length, 30 % length == 0 ? 30 : 60 }
                : new[] { 1, 3, (int)info.Parameters[0].Default, 40 };
            foreach (var value in values.Distinct())
            {
                specs.Add(info.DefaultSpec(timeframe) with { P1 = value });
            }

            if (info.UsesSource)
            {
                specs.Add(info.DefaultSpec(timeframe) with { P1 = 5, Source = info.DefaultSource == PriceSource.Close ? PriceSource.High : PriceSource.Close });
            }
        }

        return specs;
    }

    public static string Describe(FeatureSpec spec) => $"{spec.Name}({spec.P1}) {spec.Timeframe.ToCode()} {spec.Source}";

    public static IEnumerable<Timeframe> HigherTimeframes { get; } = [Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.D1];
}
