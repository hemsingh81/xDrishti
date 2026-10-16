namespace XDrishti.Indicators.Tests.Support;

/// <summary>The reference series of every <see cref="ContextValues"/> field, one entry per day of the input; NaN where the value is not ready or the day does not have its input.</summary>
internal sealed record ReferenceContextValues(
    double[] IndexTrend,
    double[] IndexAdx,
    double[] IndexVolPct,
    double[] Vix,
    double[] VixPct,
    double[] VixChg,
    double[] Breadth,
    double[] RegimeTrend,
    double[] RegimeVol,
    double[] Regime);

/// <summary>
/// The market context (P3-01c) written a second time from the definitions in the spec, with whole-array loops over the complete daily series and every window re-scanned from scratch. It shares no code with
/// <c>XDrishti.Indicators</c>: the average, ATR and ADX come from the plain references of this project (<see cref="ReferenceIndicators"/>, <see cref="ReferenceTrend"/>), the percentile ranks, the trend rule,
/// the VIX values and the regime lookup are written here. The regime code is found by looking up its words (<c>UP</c>, <c>LOW</c>) rather than by the arithmetic formula of the implementation.
/// </summary>
internal static class ReferenceContext
{
    private static readonly string[] TrendWords = ["UP", "RANGE", "DOWN"];

    private static readonly string[] VolatilityWords = ["LOW", "NORMAL", "HIGH"];

    public static ReferenceContextValues Compute(IReadOnlyList<ContextDay> days, ContextOptions options)
    {
        var count = days.Count;
        var closes = days.Select(d => d.IndexClose).ToArray();
        var bars = days.Select(d => Minutes.Bar(d.Date, 0, d.IndexClose, d.IndexHigh, d.IndexLow, d.IndexClose, 0)).ToList();
        var average = ReferenceIndicators.Sma(closes, options.TrendSmaPeriod);
        var adx = ReferenceTrend.Dmi(bars, options.AdxPeriod).Adx;
        var atr = ReferenceIndicators.Atr(bars, options.AtrPeriod);

        var trend = Nan(count);
        var atrPercent = Nan(count);
        var indexVolPct = Nan(count);
        for (var d = 0; d < count; d++)
        {
            trend[d] = Trend(closes, average, adx, d, options);
            atrPercent[d] = atr[d] / closes[d] * 100.0;
        }

        for (var d = 0; d < count; d++)
        {
            indexVolPct[d] = Rank(atrPercent, d, options.PercentileWindow);
        }

        var vix = Nan(count);
        var vixPct = Nan(count);
        var vixChg = Nan(count);
        var breadth = Nan(count);
        var regimeVol = Nan(count);
        var regime = Nan(count);
        for (var d = 0; d < count; d++)
        {
            vix[d] = days[d].VixClose ?? double.NaN;
            breadth[d] = days[d].Breadth ?? double.NaN;
            vixPct[d] = days[d].VixClose is null ? double.NaN : VixRank(days, d, options.PercentileWindow);
            if (d > 0 && days[d].VixClose is { } today && days[d - 1].VixClose is { } yesterday)
            {
                vixChg[d] = (today - yesterday) / yesterday * 100.0;
            }

            regimeVol[d] = Volatility(indexVolPct[d], vixPct[d], options);
            regime[d] = Regime(trend[d], regimeVol[d]);
        }

        return new ReferenceContextValues(trend, adx, indexVolPct, vix, vixPct, vixChg, breadth, [.. trend], regimeVol, regime);
    }

    /// <summary>+1, 0 or −1 on day d once the average, its slope and the ADX exist, else NaN (spec: the trend rule).</summary>
    private static double Trend(double[] closes, double[] average, double[] adx, int d, ContextOptions options)
    {
        var back = d - options.TrendSlopeDays;
        if (back < 0 || double.IsNaN(average[d]) || double.IsNaN(average[back]) || double.IsNaN(adx[d]))
        {
            return double.NaN;
        }

        var slope = average[d] / average[back] - 1.0;
        if (closes[d] > average[d] && slope > 0 && adx[d] >= options.AdxMin)
        {
            return 1;
        }

        if (closes[d] < average[d] && slope < 0 && adx[d] >= options.AdxMin)
        {
            return -1;
        }

        return 0;
    }

    /// <summary>The rank of x[d] among x[d−w+1 .. d]: all w values must exist.</summary>
    private static double Rank(double[] x, int d, int window)
    {
        if (d - window + 1 < 0)
        {
            return double.NaN;
        }

        var inWindow = Enumerable.Range(d - window + 1, window).Select(i => x[i]).ToList();
        return inWindow.Any(double.IsNaN) ? double.NaN : Rank(inWindow, x[d], window);
    }

    /// <summary>The rank of the VIX close of day d among the last <paramref name="window"/> days up to d that have a VIX (days without one are skipped, never filled).</summary>
    private static double VixRank(IReadOnlyList<ContextDay> days, int d, int window)
    {
        var withVix = new List<double>();
        for (var i = d; i >= 0 && withVix.Count < window; i--)
        {
            if (days[i].VixClose is { } value)
            {
                withVix.Add(value);
            }
        }

        return withVix.Count < window ? double.NaN : Rank(withVix, days[d].VixClose!.Value, window);
    }

    private static double Rank(List<double> window, double value, int size)
    {
        var below = window.Count(v => v < value);
        var equal = window.Count(v => v == value);
        return 100.0 * (below + 0.5 * equal) / size;
    }

    private static double Volatility(double indexVolPct, double vixPct, ContextOptions options)
    {
        if (double.IsNaN(indexVolPct))
        {
            return double.NaN;
        }

        // The reference adds the two percentiles as doubles, as a reader of the definition would, so a score that is exactly one third (two thirds) of 100 lands a few ulps either side of the limit: it counts as
        // on the limit when it is within a billionth of it. The context decides the same days from exact integers, with no tolerance (ContextHandTests pins both boundaries on paper).
        const double OnTheLimit = 1e-9;
        var score = double.IsNaN(vixPct) ? indexVolPct : (vixPct + indexVolPct) / 2.0;
        if (score < options.LowVolBelow - OnTheLimit)
        {
            return 0;
        }

        return score >= options.HighVolFrom - OnTheLimit ? 2 : 1;
    }

    /// <summary>The regime code as the position of its words in the two lists: UP_LOW_VOL is the first, DOWN_HIGH_VOL the ninth.</summary>
    private static double Regime(double trend, double vol)
    {
        if (double.IsNaN(trend) || double.IsNaN(vol))
        {
            return double.NaN;
        }

        var trendWord = trend > 0 ? "UP" : trend < 0 ? "DOWN" : "RANGE";
        var volWord = vol < 0.5 ? "LOW" : vol < 1.5 ? "NORMAL" : "HIGH";
        return Array.IndexOf(TrendWords, trendWord) * VolatilityWords.Length + Array.IndexOf(VolatilityWords, volWord) + 1;
    }

    private static double[] Nan(int length) => Enumerable.Repeat(double.NaN, length).ToArray();
}
