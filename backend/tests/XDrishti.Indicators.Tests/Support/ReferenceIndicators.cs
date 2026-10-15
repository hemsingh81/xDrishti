using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators.Tests.Support;

/// <summary>
/// Plain batch implementations of every indicator, written from the definitions in the spec (P3-01) with whole-array loops, windows re-summed from scratch and calendar arithmetic on <see cref="DateTimeOffset"/>.
/// They share no code with <c>XDrishti.Indicators</c>. A value that is not ready is NaN.
/// </summary>
internal static class ReferenceIndicators
{
    /// <summary>The reference series for a feature spec over the bars the indicator is fed (the bars of the spec's own timeframe).</summary>
    public static double[] Compute(FeatureSpec spec, IReadOnlyList<Bar> bars)
    {
        var n = (int)spec.P1;
        return spec.Name switch
        {
            "sma" => Sma(Pick(bars, spec.Source), n),
            "ema" => Ema(Pick(bars, spec.Source), n),
            "rsi" => Rsi(Pick(bars, spec.Source), n),
            "atr" => Atr(bars, n),
            "highest" => Extreme(Pick(bars, spec.Source), n, highest: true),
            "lowest" => Extreme(Pick(bars, spec.Source), n, highest: false),
            "vwap" => Vwap(bars),
            "rel_volume" => RelVolume(bars, n),
            "or_high" => OpeningRange(bars, n, spec.Timeframe, high: true),
            "or_low" => OpeningRange(bars, n, spec.Timeframe, high: false),
            "prev_day_open" => Session(bars, (prev, _) => prev?.Open ?? double.NaN),
            "prev_day_high" => Session(bars, (prev, _) => prev?.High ?? double.NaN),
            "prev_day_low" => Session(bars, (prev, _) => prev?.Low ?? double.NaN),
            "prev_day_close" => Session(bars, (prev, _) => prev?.Close ?? double.NaN),
            "prev_day_volume" => Session(bars, (prev, _) => prev?.Volume ?? double.NaN),
            "today_open" => Session(bars, (_, today) => today.Open),
            "today_high" => Session(bars, (_, today) => today.High),
            "today_low" => Session(bars, (_, today) => today.Low),
            "gap_pct" => Session(bars, (prev, today) => prev is null || prev.Close == 0 ? double.NaN : (today.Open - prev.Close) / prev.Close * 100.0),
            "adx" => ReferenceTrend.Dmi(bars, n).Adx,
            "plus_di" => ReferenceTrend.Dmi(bars, n).PlusDi,
            "minus_di" => ReferenceTrend.Dmi(bars, n).MinusDi,
            "macd_line" => ReferenceTrend.Macd(Pick(bars, spec.Source), n, (int)spec.P2, (int)spec.P3).Line,
            "macd_signal" => ReferenceTrend.Macd(Pick(bars, spec.Source), n, (int)spec.P2, (int)spec.P3).Signal,
            "macd_hist" => ReferenceTrend.Macd(Pick(bars, spec.Source), n, (int)spec.P2, (int)spec.P3).Histogram,
            "bb_upper" => ReferenceTrend.Bollinger(Pick(bars, spec.Source), n, spec.P2).Upper,
            "bb_middle" => ReferenceTrend.Bollinger(Pick(bars, spec.Source), n, spec.P2).Middle,
            "bb_lower" => ReferenceTrend.Bollinger(Pick(bars, spec.Source), n, spec.P2).Lower,
            "bb_width" => ReferenceTrend.Bollinger(Pick(bars, spec.Source), n, spec.P2).Width,
            "supertrend" => ReferenceTrend.Supertrend(bars, n, spec.P2, ReferenceTrend.SupertrendStart.Specified).Value,
            "supertrend_dir" => ReferenceTrend.Supertrend(bars, n, spec.P2, ReferenceTrend.SupertrendStart.Specified).Direction,
            "roc" => ReferenceTrend.Roc(Pick(bars, spec.Source), n),
            "inside_bar" => ReferencePatterns.InsideBar(bars),
            "nr" => ReferencePatterns.NarrowRange(bars, n),
            "engulfing" => ReferencePatterns.Engulfing(bars),
            "pivot_high" => ReferencePatterns.Pivots(bars, n, high: true).Price,
            "pivot_low" => ReferencePatterns.Pivots(bars, n, high: false).Price,
            "pivot_high_age" => ReferencePatterns.Pivots(bars, n, high: true).Age,
            "pivot_low_age" => ReferencePatterns.Pivots(bars, n, high: false).Age,
            "open" or "high" or "low" or "close" or "volume" => ReferencePatterns.Raw(bars, spec.Name),
            _ => throw new ArgumentException("No reference for " + spec.Name),
        };
    }

    public static double[] Pick(IReadOnlyList<Bar> bars, PriceSource source) =>
        [.. bars.Select(b => source switch { PriceSource.Open => b.Open, PriceSource.High => b.High, PriceSource.Low => b.Low, _ => b.Close })];

    public static double[] Sma(double[] x, int n)
    {
        var result = Nan(x.Length);
        for (var i = n - 1; i < x.Length; i++)
        {
            var sum = 0.0;
            for (var j = i - n + 1; j <= i; j++)
            {
                sum += x[j];
            }

            result[i] = sum / n;
        }

        return result;
    }

    public static double[] Ema(double[] x, int n)
    {
        var result = Nan(x.Length);
        if (x.Length < n)
        {
            return result;
        }

        var alpha = 2.0 / (n + 1);
        result[n - 1] = x.Take(n).Sum() / n;
        for (var i = n; i < x.Length; i++)
        {
            result[i] = alpha * x[i] + (1 - alpha) * result[i - 1];
        }

        return result;
    }

    public static double[] Rsi(double[] x, int n)
    {
        var result = Nan(x.Length);
        if (x.Length < n + 1)
        {
            return result;
        }

        double Gain(int i) => Math.Max(x[i] - x[i - 1], 0);
        double Loss(int i) => Math.Max(x[i - 1] - x[i], 0);
        var gain = Enumerable.Range(1, n).Sum(Gain) / n;
        var loss = Enumerable.Range(1, n).Sum(Loss) / n;
        result[n] = Strength(gain, loss);
        for (var i = n + 1; i < x.Length; i++)
        {
            gain = (gain * (n - 1) + Gain(i)) / n;
            loss = (loss * (n - 1) + Loss(i)) / n;
            result[i] = Strength(gain, loss);
        }

        return result;
    }

    private static double Strength(double gain, double loss) =>
        loss == 0 ? (gain > 0 ? 100 : 50) : 100 - 100 / (1 + gain / loss);

    public static double[] TrueRange(IReadOnlyList<Bar> bars)
    {
        var tr = new double[bars.Count];
        for (var i = 0; i < bars.Count; i++)
        {
            tr[i] = i == 0
                ? bars[i].High - bars[i].Low
                : new[] { bars[i].High - bars[i].Low, Math.Abs(bars[i].High - bars[i - 1].Close), Math.Abs(bars[i].Low - bars[i - 1].Close) }.Max();
        }

        return tr;
    }

    public static double[] Atr(IReadOnlyList<Bar> bars, int n)
    {
        var result = Nan(bars.Count);
        if (bars.Count < n)
        {
            return result;
        }

        var tr = TrueRange(bars);
        result[n - 1] = tr.Take(n).Sum() / n;
        for (var i = n; i < bars.Count; i++)
        {
            result[i] = (result[i - 1] * (n - 1) + tr[i]) / n;
        }

        return result;
    }

    public static double[] Extreme(double[] x, int n, bool highest)
    {
        var result = Nan(x.Length);
        for (var i = n - 1; i < x.Length; i++)
        {
            var window = x.Skip(i - n + 1).Take(n);
            result[i] = highest ? window.Max() : window.Min();
        }

        return result;
    }

    public static double[] Vwap(IReadOnlyList<Bar> bars)
    {
        var result = Nan(bars.Count);
        DateOnly? day = null;
        double priceVolume = 0;
        double volume = 0;
        for (var i = 0; i < bars.Count; i++)
        {
            var d = DateOf(bars[i]);
            if (day != d)
            {
                day = d;
                priceVolume = 0;
                volume = 0;
            }

            priceVolume += (bars[i].High + bars[i].Low + bars[i].Close) / 3 * bars[i].Volume;
            volume += bars[i].Volume;
            result[i] = volume > 0 ? priceVolume / volume : double.NaN;
        }

        return result;
    }

    public static double[] RelVolume(IReadOnlyList<Bar> bars, int n)
    {
        var result = Nan(bars.Count);
        for (var i = n; i < bars.Count; i++)
        {
            var mean = Enumerable.Range(i - n, n).Sum(j => (double)bars[j].Volume) / n;
            result[i] = mean > 0 ? bars[i].Volume / mean : double.NaN;
        }

        return result;
    }

    /// <summary>The extreme of the bars that start in the first m minutes of the session; defined once a bar reaching the last minute of the window (or a later bar) has been seen on the date.</summary>
    public static double[] OpeningRange(IReadOnlyList<Bar> bars, int minutes, Timeframe timeframe, bool high)
    {
        var length = Math.Max(1, timeframe.Minutes());
        var to = Minutes.Open + minutes;
        var result = Nan(bars.Count);
        var dates = bars.Select(DateOf).ToArray();
        var starts = bars.Select(MinuteOf).ToArray();
        var first = 0;
        for (var i = 0; i < bars.Count; i++)
        {
            if (dates[i] != dates[first])
            {
                first = i;
            }

            var sameDay = Enumerable.Range(first, i - first + 1).ToList();
            var included = sameDay.Where(j => starts[j] >= Minutes.Open && starts[j] < to).ToList();
            var reached = sameDay.Any(j => starts[j] + length >= to);
            if (included.Count > 0 && reached)
            {
                result[i] = high ? included.Max(j => bars[j].High) : included.Min(j => bars[j].Low);
            }
        }

        return result;
    }

    public sealed record DayFacts(double Open, double High, double Low, double Close, long Volume);

    private static double[] Session(IReadOnlyList<Bar> bars, Func<DayFacts?, DayFacts, double> pick)
    {
        var result = Nan(bars.Count);
        var dates = bars.Select(DateOf).ToArray();
        DayFacts Facts(int from, int to)
        {
            var list = Enumerable.Range(from, to - from + 1).Select(j => bars[j]).ToList();
            return new DayFacts(list[0].Open, list.Max(b => b.High), list.Min(b => b.Low), list[^1].Close, list.Sum(b => b.Volume));
        }

        var previous = (From: -1, To: -1);
        var current = 0;
        for (var i = 0; i < bars.Count; i++)
        {
            if (dates[i] != dates[current])
            {
                previous = (current, i - 1);
                current = i;
            }

            var before = previous.From >= 0 ? Facts(previous.From, previous.To) : null;
            result[i] = pick(before, Facts(current, i));
        }

        return result;
    }

    private static DateOnly DateOf(Bar bar) => IndiaTime.Today(DateTimeOffset.FromUnixTimeSeconds(bar.EpochSeconds));

    private static int MinuteOf(Bar bar)
    {
        var ist = IndiaTime.ToIst(DateTimeOffset.FromUnixTimeSeconds(bar.EpochSeconds));
        return ist.Hour * 60 + ist.Minute;
    }

    private static double[] Nan(int length) => Enumerable.Repeat(double.NaN, length).ToArray();
}
