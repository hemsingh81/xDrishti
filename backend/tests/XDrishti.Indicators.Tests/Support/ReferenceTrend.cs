using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators.Tests.Support;

/// <summary>
/// Plain batch references for the trend and volatility indicators of P3-01b (directional movement, MACD, Bollinger bands, Supertrend, rate of change), written from the definitions in ADR 0032 with whole-array
/// loops. They share no code with <c>XDrishti.Indicators</c>; the moving averages come from <see cref="ReferenceIndicators"/>. A value that is not ready is NaN.
/// </summary>
internal static class ReferenceTrend
{
    /// <summary>+DI, −DI and ADX of Wilder's directional movement system; the DIs from index n (bar n+1), the ADX from index 2n−1 (bar 2n).</summary>
    public static (double[] PlusDi, double[] MinusDi, double[] Adx) Dmi(IReadOnlyList<Bar> bars, int n)
    {
        var length = bars.Count;
        var plusDi = Nan(length);
        var minusDi = Nan(length);
        var adx = Nan(length);
        if (length <= n)
        {
            return (plusDi, minusDi, adx);
        }

        var plusMove = new double[length];
        var minusMove = new double[length];
        var range = new double[length];
        for (var i = 1; i < length; i++)
        {
            var up = bars[i].High - bars[i - 1].High;
            var down = bars[i - 1].Low - bars[i].Low;
            plusMove[i] = up > down && up > 0 ? up : 0;
            minusMove[i] = down > up && down > 0 ? down : 0;
            range[i] = new[] { bars[i].High - bars[i].Low, Math.Abs(bars[i].High - bars[i - 1].Close), Math.Abs(bars[i].Low - bars[i - 1].Close) }.Max();
        }

        // Wilder sums: the first is the plain sum of the first n values (bars 2 .. n+1), then s = s - s/n + x.
        double[] Smooth(double[] x)
        {
            var s = Nan(length);
            s[n] = Enumerable.Range(1, n).Sum(i => x[i]);
            for (var i = n + 1; i < length; i++)
            {
                s[i] = s[i - 1] - s[i - 1] / n + x[i];
            }

            return s;
        }

        var smoothRange = Smooth(range);
        var smoothPlus = Smooth(plusMove);
        var smoothMinus = Smooth(minusMove);
        var dx = Nan(length);
        for (var i = n; i < length; i++)
        {
            plusDi[i] = smoothRange[i] == 0 ? 0 : 100 * smoothPlus[i] / smoothRange[i];
            minusDi[i] = smoothRange[i] == 0 ? 0 : 100 * smoothMinus[i] / smoothRange[i];
            var total = plusDi[i] + minusDi[i];
            dx[i] = total == 0 ? 0 : 100 * Math.Abs(plusDi[i] - minusDi[i]) / total;
        }

        if (length >= 2 * n)
        {
            adx[2 * n - 1] = Enumerable.Range(n, n).Sum(i => dx[i]) / n;
            for (var i = 2 * n; i < length; i++)
            {
                adx[i] = (adx[i - 1] * (n - 1) + dx[i]) / n;
            }
        }

        return (plusDi, minusDi, adx);
    }

    /// <summary>The MACD line (from index slow−1), the signal (from slow+g−2) and the histogram.</summary>
    public static (double[] Line, double[] Signal, double[] Histogram) Macd(double[] x, int fast, int slow, int signal)
    {
        var length = x.Length;
        var emaFast = ReferenceIndicators.Ema(x, fast);
        var emaSlow = ReferenceIndicators.Ema(x, slow);
        var line = Nan(length);
        var signalLine = Nan(length);
        var histogram = Nan(length);
        for (var i = slow - 1; i < length; i++)
        {
            line[i] = emaFast[i] - emaSlow[i];
        }

        var first = slow - 1;
        if (length >= first + signal)
        {
            var alpha = 2.0 / (signal + 1);
            signalLine[first + signal - 1] = Enumerable.Range(first, signal).Sum(i => line[i]) / signal;
            for (var i = first + signal; i < length; i++)
            {
                signalLine[i] = alpha * line[i] + (1 - alpha) * signalLine[i - 1];
            }
        }

        for (var i = 0; i < length; i++)
        {
            histogram[i] = double.IsNaN(signalLine[i]) ? double.NaN : line[i] - signalLine[i];
        }

        return (line, signalLine, histogram);
    }

    /// <summary>Bollinger bands with the population standard deviation, recomputed from the window at every bar.</summary>
    public static (double[] Upper, double[] Middle, double[] Lower, double[] Width) Bollinger(double[] x, int n, double k)
    {
        var upper = Nan(x.Length);
        var middle = Nan(x.Length);
        var lower = Nan(x.Length);
        var width = Nan(x.Length);
        for (var i = n - 1; i < x.Length; i++)
        {
            var window = x.Skip(i - n + 1).Take(n).ToArray();
            var mean = window.Sum() / n;
            var sigma = Math.Sqrt(window.Sum(v => (v - mean) * (v - mean)) / n);
            middle[i] = mean;
            upper[i] = mean + k * sigma;
            lower[i] = mean - k * sigma;
            width[i] = mean == 0 ? double.NaN : (upper[i] - lower[i]) / mean;
        }

        return (upper, middle, lower, width);
    }

    public static double[] Roc(double[] x, int n)
    {
        var result = Nan(x.Length);
        for (var i = n; i < x.Length; i++)
        {
            result[i] = x[i - n] == 0 ? double.NaN : 100 * (x[i] - x[i - n]) / x[i - n];
        }

        return result;
    }

    /// <summary>Where a Supertrend starts; the flip rule (the close against the <b>current</b> final band) is the same for both.</summary>
    public enum SupertrendStart
    {
        /// <summary>ADR 0032: the first value is at bar n (the bar the ATR is ready) and the first direction is up.</summary>
        Specified,

        /// <summary>Skender.Stock.Indicators 2.7.3: the first value is at bar n+1 and the first direction is up when close ≥ hl2.</summary>
        Skender,
    }

    /// <summary>
    /// The Supertrend line and its direction (+1 up, −1 down) over the bars, with the average true range of <see cref="ReferenceIndicators.Atr"/> (seeded with the mean of the first n true ranges). The final bands are
    /// computed first (carry-over against the previous close), then the direction: up to down when the close is below the current final lower band, down to up when it is above the current final upper band.
    /// With <paramref name="tieFlipsDown"/> a close exactly <i>on</i> the final lower band also flips an up trend down (Skender 2.7.3 does; TradingView and ADR 0032 do not).
    /// </summary>
    public static (double[] Value, double[] Direction) Supertrend(IReadOnlyList<Bar> bars, int n, double multiplier, SupertrendStart start, bool tieFlipsDown = false)
    {
        var length = bars.Count;
        var value = Nan(length);
        var direction = Nan(length);
        var atr = ReferenceIndicators.Atr(bars, n);
        var first = start == SupertrendStart.Specified ? n - 1 : n;
        double upper = 0;
        double lower = 0;
        var up = true;
        for (var i = first; i < length; i++)
        {
            var middle = (bars[i].High + bars[i].Low) / 2;
            var basicUpper = middle + multiplier * atr[i];
            var basicLower = middle - multiplier * atr[i];
            if (i == first)
            {
                upper = basicUpper;
                lower = basicLower;
                up = start == SupertrendStart.Specified || bars[i].Close >= middle;
            }
            else
            {
                var previousUpper = upper;
                var previousLower = lower;
                var previousClose = bars[i - 1].Close;
                upper = basicUpper < previousUpper || previousClose > previousUpper ? basicUpper : previousUpper;
                lower = basicLower > previousLower || previousClose < previousLower ? basicLower : previousLower;
                if (up && (tieFlipsDown ? bars[i].Close <= lower : bars[i].Close < lower))
                {
                    up = false;
                }
                else if (!up && bars[i].Close > upper)
                {
                    up = true;
                }
            }

            value[i] = up ? lower : upper;
            direction[i] = up ? 1 : -1;
        }

        return (value, direction);
    }

    private static double[] Nan(int length) => Enumerable.Repeat(double.NaN, length).ToArray();
}
