using System.Collections.ObjectModel;
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

    private static readonly ReadOnlyCollection<IndicatorInfo> AllView = new(Entries);

    private static readonly ReadOnlyCollection<Timeframe> Supported = new([Timeframe.M1, Timeframe.M5, Timeframe.M10, Timeframe.M15, Timeframe.M30, Timeframe.H1, Timeframe.D1]);

    /// <summary>Every indicator, in a fixed order. A read-only view: it cannot be turned back into the array behind it.</summary>
    public static IReadOnlyList<IndicatorInfo> All => AllView;

    /// <summary>The timeframes a feature may read: 1m and the derived 5m, 10m, 15m, 30m, 1h and 1D. A read-only view.</summary>
    public static IReadOnlyList<Timeframe> SupportedTimeframes => Supported;

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
        var dmiPeriod = rsiPeriod;
        var macdParameters = new[]
        {
            new IndicatorParameter("fast", ParameterKind.Whole, 1, MaxPeriod, 12),
            new IndicatorParameter("slow", ParameterKind.Whole, 2, MaxPeriod, 26),
            new IndicatorParameter("signal", ParameterKind.Whole, 1, MaxPeriod, 9),
        };
        var bandParameters = new[] { period, new IndicatorParameter("k", ParameterKind.Number, 0, 100, 2) };
        var supertrendParameters = new[] { period with { Default = 10 }, new IndicatorParameter("mult", ParameterKind.Number, 0, 1_000, 3) };
        var pivotPeriod = period with { Default = 5 };
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

        AddTrend(list, dmiPeriod, macdParameters, bandParameters, supertrendParameters);
        AddPatterns(list, period, pivotPeriod);
        AddRawBars(list);

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

    /// <summary>The directional movement, MACD, Bollinger, Supertrend and rate-of-change features (ADR 0032): every output is a feature of its own, all continuous across sessions.</summary>
    private static void AddTrend(List<IndicatorInfo> list, IndicatorParameter dmiPeriod, IndicatorParameter[] macd, IndicatorParameter[] bands, IndicatorParameter[] supertrend)
    {
        list.Add(new("adx", "Wilder average directional index; the mean of the first n directional-movement values seeds it (ready at bar 2n).", [dmiPeriod], SessionBehaviour.Continuous, false, PriceSource.Close, false,
            spec => 2 * (int)spec.P1, spec => new Adx((int)spec.P1)));
        list.Add(new("plus_di", "Wilder positive directional indicator, 100 * smoothed +DM / smoothed true range (ready at bar n+1).", [dmiPeriod], SessionBehaviour.Continuous, false, PriceSource.Close, false,
            spec => (int)spec.P1 + 1, spec => new PlusDi((int)spec.P1)));
        list.Add(new("minus_di", "Wilder negative directional indicator, 100 * smoothed -DM / smoothed true range (ready at bar n+1).", [dmiPeriod], SessionBehaviour.Continuous, false, PriceSource.Close, false,
            spec => (int)spec.P1 + 1, spec => new MinusDi((int)spec.P1)));

        list.Add(new("macd_line", "MACD line: ema(fast) - ema(slow) of the source (ready at bar slow).", macd, SessionBehaviour.Continuous, true, PriceSource.Close, false,
            spec => (int)spec.P2, spec => new MacdLine((int)spec.P1, (int)spec.P2, (int)spec.P3, spec.Source), FastBelowSlow));
        list.Add(new("macd_signal", "MACD signal: ema(signal) of the MACD line, seeded with the mean of the line's first values (ready at bar slow + signal - 1).", macd, SessionBehaviour.Continuous, true, PriceSource.Close, false,
            spec => (int)spec.P2 + (int)spec.P3 - 1, spec => new MacdSignal((int)spec.P1, (int)spec.P2, (int)spec.P3, spec.Source), FastBelowSlow));
        list.Add(new("macd_hist", "MACD histogram: MACD line - MACD signal (ready at bar slow + signal - 1).", macd, SessionBehaviour.Continuous, true, PriceSource.Close, false,
            spec => (int)spec.P2 + (int)spec.P3 - 1, spec => new MacdHist((int)spec.P1, (int)spec.P2, (int)spec.P3, spec.Source), FastBelowSlow));

        list.Add(new("bb_upper", "Upper Bollinger band: the mean of the last n values plus k population standard deviations (ready at bar n).", bands, SessionBehaviour.Continuous, true, PriceSource.Close, false,
            spec => (int)spec.P1, spec => new BbUpper((int)spec.P1, spec.P2, spec.Source), PositiveSecond("k")));
        list.Add(new("bb_middle", "Middle Bollinger band: the mean of the last n values (ready at bar n).", bands, SessionBehaviour.Continuous, true, PriceSource.Close, false,
            spec => (int)spec.P1, spec => new BbMiddle((int)spec.P1, spec.P2, spec.Source), PositiveSecond("k")));
        list.Add(new("bb_lower", "Lower Bollinger band: the mean of the last n values minus k population standard deviations (ready at bar n).", bands, SessionBehaviour.Continuous, true, PriceSource.Close, false,
            spec => (int)spec.P1, spec => new BbLower((int)spec.P1, spec.P2, spec.Source), PositiveSecond("k")));
        list.Add(new("bb_width", "Bollinger band width: (upper - lower) / middle (ready at bar n).", bands, SessionBehaviour.Continuous, true, PriceSource.Close, false,
            spec => (int)spec.P1, spec => new BbWidth((int)spec.P1, spec.P2, spec.Source), PositiveSecond("k")));

        list.Add(new("supertrend", "Supertrend line: the final lower band while the trend is up, the final upper band while it is down (ready at bar n).", supertrend, SessionBehaviour.Continuous, false, PriceSource.Close, false,
            spec => (int)spec.P1, spec => new Supertrend((int)spec.P1, spec.P2), PositiveSecond("mult")));
        list.Add(new("supertrend_dir", "Supertrend direction: +1 while the trend is up, -1 while it is down (ready at bar n).", supertrend, SessionBehaviour.Continuous, false, PriceSource.Close, false,
            spec => (int)spec.P1, spec => new SupertrendDirection((int)spec.P1, spec.P2), PositiveSecond("mult")));

        list.Add(new("roc", "Rate of change in percent: 100 * (x - x[n]) / x[n] (ready at bar n+1; not ready while x[n] is 0).", [new IndicatorParameter("n", ParameterKind.Whole, 1, MaxPeriod, 10)], SessionBehaviour.Continuous, true, PriceSource.Close, false,
            spec => (int)spec.P1 + 1, spec => new Roc((int)spec.P1, spec.Source)));
    }

    /// <summary>Candle patterns and swing structure (ADR 0032): stateless on the closed bars of the timeframe, or the latest confirmed swing point.</summary>
    private static void AddPatterns(List<IndicatorInfo> list, IndicatorParameter period, IndicatorParameter pivotPeriod)
    {
        list.Add(new("inside_bar", "1 when the bar's high is below the previous high and its low above the previous low, else 0.", [], SessionBehaviour.Continuous, false, PriceSource.Close, false,
            _ => 2, _ => new InsideBar()));
        list.Add(new("nr", "1 when the bar's range is strictly smaller than the ranges of the previous n-1 bars (narrow range n), else 0.", [period with { Min = 2, Default = 7 }], SessionBehaviour.Continuous, false, PriceSource.Close, false,
            spec => (int)spec.P1, spec => new NarrowRange((int)spec.P1)));
        list.Add(new("engulfing", "+1 for a bullish and -1 for a bearish engulfing candle (judged on the bodies), else 0.", [], SessionBehaviour.Continuous, false, PriceSource.Close, false,
            _ => 2, _ => new Engulfing()));

        list.Add(new("pivot_high", "High of the most recent confirmed swing high: strictly above the n bars before and after it; known n bars late.", [pivotPeriod], SessionBehaviour.Continuous, false, PriceSource.Close, false,
            spec => 2 * (int)spec.P1 + 1, spec => new PivotHigh((int)spec.P1)));
        list.Add(new("pivot_low", "Low of the most recent confirmed swing low: strictly below the n bars before and after it; known n bars late.", [pivotPeriod], SessionBehaviour.Continuous, false, PriceSource.Close, false,
            spec => 2 * (int)spec.P1 + 1, spec => new PivotLow((int)spec.P1)));
        list.Add(new("pivot_high_age", "Bars since the swing high that pivot_high(n) reports.", [pivotPeriod], SessionBehaviour.Continuous, false, PriceSource.Close, false,
            spec => 2 * (int)spec.P1 + 1, spec => new PivotHighAge((int)spec.P1)));
        list.Add(new("pivot_low_age", "Bars since the swing low that pivot_low(n) reports.", [pivotPeriod], SessionBehaviour.Continuous, false, PriceSource.Close, false,
            spec => 2 * (int)spec.P1 + 1, spec => new PivotLowAge((int)spec.P1)));
    }

    /// <summary>The raw bar features: the value of the last closed bar of the timeframe.</summary>
    private static void AddRawBars(List<IndicatorInfo> list)
    {
        foreach (var (name, source, text) in new[]
        {
            ("open", PriceSource.Open, "Open of the last closed bar of the timeframe."),
            ("high", PriceSource.High, "High of the last closed bar of the timeframe."),
            ("low", PriceSource.Low, "Low of the last closed bar of the timeframe."),
            ("close", PriceSource.Close, "Close of the last closed bar of the timeframe."),
            ("volume", PriceSource.Volume, "Volume of the last closed bar of the timeframe."),
        })
        {
            var captured = source;
            list.Add(new IndicatorInfo(name, text, [], SessionBehaviour.Continuous, false, PriceSource.Close, false, _ => 1, _ => new RawBar(captured)));
        }
    }

    private static SpecProblem? FastBelowSlow(FeatureSpec spec) =>
        spec.P1 < spec.P2 ? null : new SpecProblem(0, "'fast' must be below 'slow'.");

    private static Func<FeatureSpec, SpecProblem?> PositiveSecond(string name) =>
        spec => spec.P2 > 0 ? null : new SpecProblem(1, $"'{name}' must be above 0.");

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

    private static SpecProblem? WholeBars(FeatureSpec spec) =>
        (int)spec.P1 % BarMinutes(spec) == 0 ? null : new SpecProblem(0, $"The opening range must be a whole number of {spec.Timeframe.ToCode()} bars.");
}
