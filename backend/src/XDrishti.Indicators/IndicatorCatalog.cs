using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators;

/// <summary>
/// The indicators this library offers, with the metadata the DSL validator and the feature frame need (ADR 0031): parameters with ranges and defaults, warm-up bars as a function of the parameters, session
/// behaviour and a factory. A test fails if an indicator class is not listed here or if its measured first ready bar differs from the warm-up declared here.
/// </summary>
public static class IndicatorCatalog
{
    private const int MaxPeriod = 10_000;

    private static readonly IndicatorInfo[] Entries = Build();

    private static readonly Dictionary<string, IndicatorInfo> ByName = Index(Entries);

    /// <summary>Every indicator, in a fixed order.</summary>
    public static IReadOnlyList<IndicatorInfo> All => Entries;

    /// <summary>The timeframes a feature may read: 1m and the derived 5m, 10m, 15m, 30m, 1h and 1D.</summary>
    public static IReadOnlyList<Timeframe> SupportedTimeframes { get; } = [Timeframe.M1, Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.D1];

    public static bool TryGet(string? name, out IndicatorInfo info)
    {
        if (name is not null && ByName.TryGetValue(name, out var found))
        {
            info = found;
            return true;
        }

        info = null!;
        return false;
    }

    internal static bool IsSupported(Timeframe timeframe) => timeframe is Timeframe.M1 or Timeframe.M5 or Timeframe.M10 or Timeframe.M15 or Timeframe.M30 or Timeframe.H1 or Timeframe.D1;

    private static IndicatorInfo[] Build()
    {
        var period = new IndicatorParameter("n", ParameterKind.Whole, 1, MaxPeriod, 20);
        var rsiPeriod = period with { Default = 14 };
        var minutes = new IndicatorParameter("minutes", ParameterKind.Whole, 1, SessionClock.RegularBars, 15);
        var list = new List<IndicatorInfo>
        {
            new("sma", "Simple moving average of the last n values of the source.", [period], SessionBehaviour.Continuous, true, PriceSource.Close, false,
                spec => (int)spec.P1, spec => new Sma((int)spec.P1, spec.Source)),
            new("ema", "Exponential moving average (alpha = 2/(n+1)), seeded with the simple mean of the first n values.", [period], SessionBehaviour.Continuous, true, PriceSource.Close, false,
                spec => (int)spec.P1, spec => new Ema((int)spec.P1, spec.Source)),
            new("rsi", "Wilder relative strength index of the source.", [rsiPeriod], SessionBehaviour.Continuous, true, PriceSource.Close, false,
                spec => (int)spec.P1 + 1, spec => new Rsi((int)spec.P1, spec.Source)),
            new("atr", "Wilder average true range.", [rsiPeriod], SessionBehaviour.Continuous, false, PriceSource.Close, false,
                spec => (int)spec.P1, spec => new Atr((int)spec.P1)),
            new("highest", "Highest value of the source over the last n bars, current bar included.", [period], SessionBehaviour.Continuous, true, PriceSource.High, false,
                spec => (int)spec.P1, spec => new Highest((int)spec.P1, spec.Source)),
            new("lowest", "Lowest value of the source over the last n bars, current bar included.", [period], SessionBehaviour.Continuous, true, PriceSource.Low, false,
                spec => (int)spec.P1, spec => new Lowest((int)spec.P1, spec.Source)),
            new("vwap", "Volume-weighted average price of the typical price since the session open.", [], SessionBehaviour.PerSession, false, PriceSource.Close, true,
                _ => 1, _ => new Vwap()),
            new("rel_volume", "Volume divided by the mean volume of the previous n bars (current bar excluded).", [period], SessionBehaviour.Continuous, false, PriceSource.Close, false,
                spec => (int)spec.P1 + 1, spec => new RelVolume((int)spec.P1)),
            new("or_high", "Highest high of the first m minutes of the session; ready once they have closed.", [minutes], SessionBehaviour.PerSession, false, PriceSource.Close, true,
                spec => (int)spec.P1 / BarMinutes(spec), spec => new OrHigh((int)spec.P1, BarMinutes(spec)), WholeBars),
            new("or_low", "Lowest low of the first m minutes of the session; ready once they have closed.", [minutes], SessionBehaviour.PerSession, false, PriceSource.Close, true,
                spec => (int)spec.P1 / BarMinutes(spec), spec => new OrLow((int)spec.P1, BarMinutes(spec)), WholeBars),
        };

        foreach (var (name, field) in new[]
        {
            ("prev_day_open", SessionField.PrevOpen), ("prev_day_high", SessionField.PrevHigh), ("prev_day_low", SessionField.PrevLow), ("prev_day_close", SessionField.PrevClose),
            ("prev_day_volume", SessionField.PrevVolume), ("today_open", SessionField.TodayOpen), ("today_high", SessionField.TodayHigh), ("today_low", SessionField.TodayLow),
            ("gap_pct", SessionField.GapPct),
        })
        {
            var captured = field;
            var text = field switch
            {
                SessionField.GapPct => "Opening gap in percent: (today open - previous session close) / previous session close * 100.",
                SessionField.TodayOpen or SessionField.TodayHigh or SessionField.TodayLow => "Today's value so far, over the bars of the session closed up to now.",
                _ => "Value of the previous session that had bars (weekends and holidays are skipped by the data).",
            };
            list.Add(new IndicatorInfo(
                name, text, [], SessionBehaviour.PerSession, false, PriceSource.Close, false,
                spec => captured is SessionField.TodayOpen or SessionField.TodayHigh or SessionField.TodayLow ? 1 : BarsPerSession(spec.Timeframe) + 1,
                spec => new SessionContext(captured, BarsPerSession(spec.Timeframe))));
        }

        return [.. list];
    }

    private static Dictionary<string, IndicatorInfo> Index(IndicatorInfo[] entries)
    {
        var map = new Dictionary<string, IndicatorInfo>(entries.Length, StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            map.Add(entry.Name, entry);
        }

        return map;
    }

    /// <summary>The length in minutes of one bar of the timeframe (1 for 1m); a day is treated as a single bar.</summary>
    private static int BarMinutes(FeatureSpec spec) => Math.Max(1, spec.Timeframe.Minutes());

    /// <summary>How many bars of the timeframe a full regular session has (the last one clipped at the close).</summary>
    private static int BarsPerSession(Timeframe timeframe) =>
        timeframe == Timeframe.D1 ? 1 : (SessionClock.RegularBars + timeframe.Minutes() - 1) / timeframe.Minutes();

    private static string? WholeBars(FeatureSpec spec) =>
        (int)spec.P1 % BarMinutes(spec) == 0 ? null : $"The opening range must be a whole number of {spec.Timeframe.ToCode()} bars.";
}
