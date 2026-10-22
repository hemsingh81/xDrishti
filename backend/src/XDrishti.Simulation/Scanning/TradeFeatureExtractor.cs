using XDrishti.Domain.Learning;
using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Indicators;

namespace XDrishti.Simulation.Scanning;

/// <summary>
/// The decision-time features of trades (spec P6-04, ADR 0066): for each decision a feature frame of the trade's timeframe is stepped with the instrument's adjusted 1-minute bars and read the moment before the first bar that starts at or after the
/// decision instant. The loop stops at the last decision of the timeframe, so a bar after it is never touched: no feature can see the future by construction (and a property test cuts the bars to prove it). Pure calculation: no I/O, no clock.
/// <para>
/// The bars are every stored bar of the instrument from its first one, adjusted with the applied corporate actions as the replay does; sessions the replay left out (a halt, a discontinuity) are fed here, so a warm-up can differ slightly from what the scanner saw
/// (stated limit). The market-context features are NaN unless a <see cref="MarketContext"/> is given.
/// </para>
/// </summary>
public static class TradeFeatureExtractor
{
    private const int Period = 14;

    /// <summary>The stored values (<see cref="TradeFeatureCatalog.StoredCount"/> of them, catalog order, NaN where unknown) by trade key.</summary>
    /// <param name="rawBars">The stored 1-minute bars of the instrument, raw, strictly ascending.</param>
    /// <param name="factors">The applied corporate actions.</param>
    /// <param name="calendar">The trading calendar.</param>
    /// <param name="decisions">The trades of the instrument (any timeframe).</param>
    /// <param name="context">The daily market context, or null while it is not loaded.</param>
    /// <param name="expiryDays">The expiry dates, or null when unknown (then <c>is_expiry_day</c> is NaN).</param>
    public static IReadOnlyDictionary<string, double[]> Extract(
        IReadOnlyList<Bar> rawBars,
        AdjustmentFactors factors,
        TradingCalendar calendar,
        IReadOnlyList<TradeFeatureDecision> decisions,
        MarketContext? context = null,
        IReadOnlySet<DateOnly>? expiryDays = null)
    {
        ArgumentNullException.ThrowIfNull(rawBars);
        ArgumentNullException.ThrowIfNull(factors);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(decisions);
        var result = new Dictionary<string, double[]>(decisions.Count, StringComparer.Ordinal);
        foreach (var group in decisions.GroupBy(d => d.Timeframe))
        {
            Run(group.Key, [.. group.OrderBy(d => d.DecisionEpochSeconds).ThenBy(d => d.TradeKey, StringComparer.Ordinal)], rawBars, factors, calendar, context, expiryDays, result);
        }

        return result;
    }

    private static void Run(Timeframe tf, List<TradeFeatureDecision> points, IReadOnlyList<Bar> raw, AdjustmentFactors factors, TradingCalendar calendar, MarketContext? context, IReadOnlySet<DateOnly>? expiry, Dictionary<string, double[]> result)
    {
        var specs = new List<FeatureSpec>();
        int H(string name, double p1 = 0, double p2 = 0, double p3 = 0, Timeframe? frameTf = null, bool needsContext = false, bool needsExpiry = false)
        {
            var spec = new FeatureSpec(name, frameTf ?? tf, PriceSource.Default, p1, p2, p3);
            if ((needsContext && context is null) || (needsExpiry && expiry is null) || !IndicatorCatalog.TryGet(name, out var info) || !info.Supports(spec.Timeframe))
            {
                return -1;
            }

            var existing = specs.IndexOf(spec);
            if (existing >= 0)
            {
                return existing;
            }

            specs.Add(spec);
            return specs.Count - 1;
        }

        var close = H("close");
        var open = H("open");
        var high = H("high");
        var low = H("low");
        var h = new[]
        {
            H("rsi", Period), H("atr", Period), H("adx", Period), H("plus_di", Period), H("minus_di", Period), H("ema", 20), H("ema", 50), H("macd_hist", 12, 26, 9), H("bb_width", 20, 2), H("bb_upper", 20, 2), H("bb_lower", 20, 2),
            H("roc", 10), H("rel_volume", 20), H("vwap"), H("highest", 20), H("lowest", 20), H("supertrend_dir", 10, 3), H("gap_pct"), H("prev_day_close"), H("today_low"), H("today_high"), H("prev_day_high"), H("prev_day_low"), H("today_open"),
        };
        var date = new[]
        {
            H("is_expiry_day", frameTf: Timeframe.M1, needsExpiry: true), H("index_trend", frameTf: Timeframe.M1, needsContext: true), H("index_adx", frameTf: Timeframe.M1, needsContext: true), H("index_vol_pct", frameTf: Timeframe.M1, needsContext: true),
            H("vix", frameTf: Timeframe.M1, needsContext: true), H("vix_pct", frameTf: Timeframe.M1, needsContext: true), H("vix_chg", frameTf: Timeframe.M1, needsContext: true), H("breadth", frameTf: Timeframe.M1, needsContext: true),
            H("regime", frameTf: Timeframe.M1, needsContext: true),
        };

        var created = FeatureFrame.Create(specs, new FrameOptions(calendar, 1, context, expiry));
        if (created.IsFailure)
        {
            throw new InvalidOperationException("The feature frame could not be built: " + created.Error.Message);
        }

        var frame = created.Value;
        double V(int handle) => handle < 0 ? double.NaN : frame[handle];
        var next = 0;
        var cachedDate = DateOnly.MinValue;
        var cachedFactor = 1.0;
        foreach (var bar in raw)
        {
            while (next < points.Count && bar.EpochSeconds >= points[next].DecisionEpochSeconds)
            {
                Capture(points[next++]);
            }

            if (next >= points.Count)
            {
                return;
            }

            var day = SessionClock.DateOf(bar.EpochSeconds);
            if (day != cachedDate)
            {
                cachedDate = day;
                cachedFactor = factors.FactorAt(day);
            }

            frame.Step(cachedFactor == 1.0 ? bar : new Bar(bar.EpochSeconds, AdjustmentFactors.Price(bar.Open, cachedFactor), AdjustmentFactors.Price(bar.High, cachedFactor), AdjustmentFactors.Price(bar.Low, cachedFactor), AdjustmentFactors.Price(bar.Close, cachedFactor), AdjustmentFactors.Volume(bar.Volume, cachedFactor)));
        }

        while (next < points.Count)
        {
            Capture(points[next++]);
        }

        void Capture(TradeFeatureDecision d)
        {
            var c = V(close);
            var o = V(open);
            var hi = V(high);
            var lo = V(low);
            var v = new double[TradeFeatureCatalog.StoredCount];
            v[0] = V(h[0]);
            v[1] = Ratio(V(h[1]), c);
            v[2] = V(h[2]);
            v[3] = V(h[3]);
            v[4] = V(h[4]);
            v[5] = Pct(c, V(h[5]));
            v[6] = Pct(c, V(h[6]));
            v[7] = Ratio(V(h[7]), c);
            v[8] = V(h[8]);
            v[9] = Position(c, V(h[10]), V(h[9]));
            v[10] = V(h[11]);
            v[11] = V(h[12]);
            v[12] = Pct(c, V(h[13]));
            v[13] = Position(c, V(h[15]), V(h[14]));
            v[14] = V(h[16]);
            v[15] = V(h[17]);
            v[16] = Pct(c, V(h[18]));
            v[17] = Position(c, V(h[19]), V(h[20]));
            v[18] = Ratio(V(h[21]) - V(h[22]), V(h[18]));
            v[19] = Pct(c, V(h[23]));
            v[20] = Ratio(hi - lo, c);
            v[21] = Ratio(c - o, c);
            var decisionDay = SessionClock.DateOf(d.DecisionEpochSeconds - 60);
            v[22] = ((int)decisionDay.DayOfWeek + 6) % 7 + 1;
            for (var i = 0; i < date.Length; i++)
            {
                v[23 + i] = V(date[i]);
            }

            v[32] = SessionClock.MinuteOfDay(d.DecisionEpochSeconds);
            v[33] = d.Side == Side.Buy ? 1 : -1;
            var risk = Math.Abs(c - d.StopPrice);
            v[34] = Ratio(risk, c);
            v[35] = d.TargetPrice is { } target && risk > 0 ? Math.Abs(target - c) / risk : double.NaN;
            result[d.TradeKey] = v;
        }
    }

    // 100 * a / b, unknown when either is unknown or b is not positive
    private static double Ratio(double a, double b) => double.IsFinite(a) && double.IsFinite(b) && b > 0 ? 100 * a / b : double.NaN;

    // percent distance of x above y
    private static double Pct(double x, double y) => double.IsFinite(x) && double.IsFinite(y) && y > 0 ? ((x / y) - 1) * 100 : double.NaN;

    // where x sits between lo (0) and hi (1); unknown on a flat range
    private static double Position(double x, double lo, double hi) => double.IsFinite(x) && double.IsFinite(lo) && double.IsFinite(hi) && hi > lo ? (x - lo) / (hi - lo) : double.NaN;
}
