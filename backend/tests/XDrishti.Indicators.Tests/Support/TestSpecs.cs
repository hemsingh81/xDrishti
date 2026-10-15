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

            specs.AddRange(VariantsOf(info, timeframe, length));
        }

        return specs;
    }

    /// <summary>The (fast, slow, signal) sets, the (n, k) band sets and the (n, mult) Supertrend sets that the multi-parameter indicators are tested with: small and edge ones included.</summary>
    public static IReadOnlyList<(int Fast, int Slow, int Signal)> MacdSets { get; } = [(2, 3, 1), (3, 6, 4), (12, 26, 9), (5, 40, 3)];

    public static IReadOnlyList<(int N, double K)> BandSets { get; } = [(1, 2), (2, 1.5), (5, 2), (20, 2), (40, 0.5)];

    public static IReadOnlyList<(int N, double Mult)> SupertrendSets { get; } = [(1, 1), (3, 2), (10, 3), (14, 0.5)];

    private static readonly int[] DmiPeriods = [1, 2, 3, 14];

    private static readonly int[] RocPeriods = [1, 3, 10, 40];

    private static readonly int[] NarrowRangePeriods = [2, 3, 7, 40];

    private static readonly int[] PivotPeriods = [1, 2, 5, 12];

    private static IEnumerable<FeatureSpec> VariantsOf(IndicatorInfo info, Timeframe timeframe, int length)
    {
        var defaults = info.DefaultSpec(timeframe);
        var other = defaults.Source == PriceSource.Close ? PriceSource.High : PriceSource.Close;
        switch (info.Name)
        {
            case "adx" or "plus_di" or "minus_di":
                return DmiPeriods.Select(n => defaults with { P1 = n });
            case "macd_line" or "macd_signal" or "macd_hist":
                return MacdSets.Select(m => defaults with { P1 = m.Fast, P2 = m.Slow, P3 = m.Signal })
                    .Append(defaults with { P1 = 3, P2 = 6, P3 = 4, Source = PriceSource.High });
            case "bb_upper" or "bb_middle" or "bb_lower" or "bb_width":
                return BandSets.Select(b => defaults with { P1 = b.N, P2 = b.K })
                    .Append(defaults with { P1 = 5, P2 = 2, Source = PriceSource.Low });
            case "supertrend" or "supertrend_dir":
                return SupertrendSets.Select(b => defaults with { P1 = b.N, P2 = b.Mult });
            case "roc":
                return RocPeriods.Select(n => defaults with { P1 = n }).Append(defaults with { P1 = 5, Source = other });
            case "nr":
                return NarrowRangePeriods.Select(n => defaults with { P1 = n });
            case "pivot_high" or "pivot_low" or "pivot_high_age" or "pivot_low_age":
                return PivotPeriods.Select(n => defaults with { P1 = n });
        }

        if (info.Parameters.Count == 0)
        {
            return [defaults];
        }

        var values = info.Name.StartsWith("or_", StringComparison.Ordinal)
            ? new[] { length, 2 * length, 30 % length == 0 ? 30 : 60 }
            : new[] { 1, 3, (int)info.Parameters[0].Default, 40 };
        var list = values.Distinct().Select(value => defaults with { P1 = value }).ToList();
        if (info.UsesSource)
        {
            list.Add(defaults with { P1 = 5, Source = other });
        }

        return list;
    }

    public static string Describe(FeatureSpec spec) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{spec.Name}({spec.P1}, {spec.P2}, {spec.P3}) {spec.Timeframe.ToCode()} {spec.Source}");

    public static IEnumerable<Timeframe> HigherTimeframes { get; } = [Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.D1];
}
