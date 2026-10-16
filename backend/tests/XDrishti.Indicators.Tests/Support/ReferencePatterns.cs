using XDrishti.Domain.Market.Bars;

namespace XDrishti.Indicators.Tests.Support;

/// <summary>
/// Plain batch references for the candle patterns, the swing pivots and the raw bar features of P3-01b, written from the definitions in ADR 0032 (brute-force windows, no state). A value that is not ready is NaN.
/// </summary>
internal static class ReferencePatterns
{
    public static double[] InsideBar(IReadOnlyList<Bar> bars)
    {
        var result = Nan(bars.Count);
        for (var i = 1; i < bars.Count; i++)
        {
            result[i] = bars[i].High < bars[i - 1].High && bars[i].Low > bars[i - 1].Low ? 1 : 0;
        }

        return result;
    }

    public static double[] NarrowRange(IReadOnlyList<Bar> bars, int n)
    {
        var result = Nan(bars.Count);
        for (var i = n - 1; i < bars.Count; i++)
        {
            var range = bars[i].High - bars[i].Low;
            var narrowest = true;
            for (var j = i - n + 1; j < i; j++)
            {
                narrowest &= range < bars[j].High - bars[j].Low;
            }

            result[i] = narrowest ? 1 : 0;
        }

        return result;
    }

    public static double[] Engulfing(IReadOnlyList<Bar> bars)
    {
        var result = Nan(bars.Count);
        for (var i = 1; i < bars.Count; i++)
        {
            var previous = bars[i - 1];
            var current = bars[i];
            var previousBody = Math.Abs(previous.Close - previous.Open);
            var currentBody = Math.Abs(current.Close - current.Open);
            result[i] = 0;
            if (previousBody > 0 && currentBody > 0)
            {
                if (previous.Close < previous.Open && current.Close > current.Open && current.Open <= previous.Close && current.Close >= previous.Open)
                {
                    result[i] = 1;
                }
                else if (previous.Close > previous.Open && current.Close < current.Open && current.Open >= previous.Close && current.Close <= previous.Open)
                {
                    result[i] = -1;
                }
            }
        }

        return result;
    }

    /// <summary>The price of the latest pivot confirmed by each bar and the bars since it. A bar j is a pivot when it is strictly beyond every one of the n bars before and the n bars after it; bar t sees it when j + n ≤ t.</summary>
    public static (double[] Price, double[] Age) Pivots(IReadOnlyList<Bar> bars, int n, bool high)
    {
        var length = bars.Count;
        double Level(int i) => high ? bars[i].High : bars[i].Low;
        var price = Nan(length);
        var age = Nan(length);
        var isPivot = new bool[length];
        for (var j = n; j + n < length; j++)
        {
            var pivot = true;
            for (var k = j - n; k <= j + n; k++)
            {
                if (k != j)
                {
                    pivot &= high ? Level(j) > Level(k) : Level(j) < Level(k);
                }
            }

            isPivot[j] = pivot;
        }

        for (var t = 0; t < length; t++)
        {
            for (var j = t - n; j >= n; j--)
            {
                if (isPivot[j])
                {
                    price[t] = Level(j);
                    age[t] = t - j;
                    break;
                }
            }
        }

        return (price, age);
    }

    public static double[] Raw(IReadOnlyList<Bar> bars, string name) => [.. bars.Select(b => name switch
    {
        "open" => b.Open,
        "high" => b.High,
        "low" => b.Low,
        "close" => b.Close,
        "volume" => (double)b.Volume,
        _ => throw new ArgumentException("No raw bar feature " + name),
    })];

    private static double[] Nan(int length) => Enumerable.Repeat(double.NaN, length).ToArray();
}
