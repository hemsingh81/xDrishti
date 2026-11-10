namespace XDrishti.Domain.Baskets.Derived;

/// <summary>Stateless indicator maths over arrays. Insufficient input gives NaN, which the evaluators turn into a named exclusion.</summary>
public static class IndicatorKit
{
    /// <summary>Trading days in a year; volatility is annualised with its square root (15.8745).</summary>
    public const double TradingDays = 252;

    public static readonly double AnnualisationFactor = Math.Sqrt(TradingDays);

    /// <summary>The true range: the largest of the day's range and the two gaps to the previous close.</summary>
    public static double TrueRange(double high, double low, double previousClose) =>
        Math.Max(high - low, Math.Max(Math.Abs(high - previousClose), Math.Abs(low - previousClose)));

    /// <summary>One Wilder smoothing step: <c>(previous x (n - 1) + current) / n</c>.</summary>
    public static double WilderStep(double previous, double current, int n) => ((previous * (n - 1)) + current) / n;

    /// <summary>The Wilder mean of a series: the plain mean of the first n values, then Wilder steps. NaN with fewer than n values.</summary>
    public static double WilderMean(ReadOnlySpan<double> values, int n)
    {
        if (values.Length < n)
        {
            return double.NaN;
        }

        var mean = Mean(values[..n]);
        for (var i = n; i < values.Length; i++)
        {
            mean = WilderStep(mean, values[i], n);
        }

        return mean;
    }

    /// <summary>The Wilder ATR over the bars (needs n + 1 bars).</summary>
    public static double Atr(ReadOnlySpan<Bar> bars, int n)
    {
        if (bars.Length < n + 1)
        {
            return double.NaN;
        }

        var atr = 0.0;
        for (var i = 1; i <= n; i++)
        {
            atr += TrueRange(bars[i].High, bars[i].Low, bars[i - 1].Close);
        }

        atr /= n;
        for (var i = n + 1; i < bars.Length; i++)
        {
            atr = WilderStep(atr, TrueRange(bars[i].High, bars[i].Low, bars[i - 1].Close), n);
        }

        return atr;
    }

    /// <summary>The last <paramref name="count"/> simple daily returns (fewer when the bars run out).</summary>
    public static double[] Returns(ReadOnlySpan<Bar> bars, int count)
    {
        var take = Math.Min(count, Math.Max(bars.Length - 1, 0));
        var result = new double[take];
        for (var i = 0; i < take; i++)
        {
            var at = bars.Length - take + i;
            result[i] = (bars[at].Close / bars[at - 1].Close) - 1;
        }

        return result;
    }

    public static double Mean(ReadOnlySpan<double> values)
    {
        if (values.IsEmpty)
        {
            return double.NaN;
        }

        var sum = 0.0;
        foreach (var v in values)
        {
            sum += v;
        }

        return sum / values.Length;
    }

    /// <summary>The sample variance (n - 1).</summary>
    public static double Variance(ReadOnlySpan<double> values)
    {
        if (values.Length < 2)
        {
            return double.NaN;
        }

        var mean = Mean(values);
        var sum = 0.0;
        foreach (var v in values)
        {
            sum += (v - mean) * (v - mean);
        }

        return sum / (values.Length - 1);
    }

    public static double StdDev(ReadOnlySpan<double> values) => Math.Sqrt(Variance(values));

    /// <summary>The annualised volatility of daily returns: stdev x sqrt(252).</summary>
    public static double RealisedVolatility(ReadOnlySpan<double> dailyReturns) => StdDev(dailyReturns) * AnnualisationFactor;

    /// <summary>The median; for an even count the mean of the two middle values.</summary>
    public static double Median(ReadOnlySpan<double> values)
    {
        if (values.IsEmpty)
        {
            return double.NaN;
        }

        var sorted = values.ToArray();
        Array.Sort(sorted);
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    /// <summary>The median of close x volume (rupees) over the last n bars.</summary>
    public static double MedianTradedValue(ReadOnlySpan<Bar> bars, int n)
    {
        if (bars.Length < n)
        {
            return double.NaN;
        }

        var values = new double[n];
        for (var i = 0; i < n; i++)
        {
            var b = bars[bars.Length - n + i];
            values[i] = b.Close * b.Volume;
        }

        return Median(values);
    }

    /// <summary>Kaufman efficiency ratio over n steps (n + 1 closes): net change over the path length; 0 for a flat path.</summary>
    public static double EfficiencyRatio(ReadOnlySpan<Bar> bars, int n)
    {
        if (bars.Length < n + 1)
        {
            return double.NaN;
        }

        var start = bars.Length - n - 1;
        var path = 0.0;
        for (var i = start + 1; i < bars.Length; i++)
        {
            path += Math.Abs(bars[i].Close - bars[i - 1].Close);
        }

        return path == 0 ? 0 : Math.Abs(bars[^1].Close - bars[start].Close) / path;
    }

    /// <summary>The directional index <c>100 x |+DI - -DI| / (+DI + -DI)</c>.</summary>
    public static double Dx(double plusDi, double minusDi) => plusDi + minusDi == 0 ? 0 : 100 * Math.Abs(plusDi - minusDi) / (plusDi + minusDi);

    /// <summary>The Wilder ADX over the bars (needs 2n bars): Wilder-smoothed DM and TR, then the Wilder mean of the DX values.</summary>
    public static double Adx(ReadOnlySpan<Bar> bars, int n)
    {
        if (bars.Length < (2 * n) + 1)
        {
            return double.NaN;
        }

        double smTr = 0, smPlus = 0, smMinus = 0;
        var dx = new List<double>();
        for (var i = 1; i < bars.Length; i++)
        {
            var up = bars[i].High - bars[i - 1].High;
            var down = bars[i - 1].Low - bars[i].Low;
            var plus = up > down && up > 0 ? up : 0;
            var minus = down > up && down > 0 ? down : 0;
            var tr = TrueRange(bars[i].High, bars[i].Low, bars[i - 1].Close);
            if (i <= n)
            {
                smTr += tr;
                smPlus += plus;
                smMinus += minus;
            }
            else
            {
                smTr = smTr - (smTr / n) + tr;
                smPlus = smPlus - (smPlus / n) + plus;
                smMinus = smMinus - (smMinus / n) + minus;
            }

            if (i >= n)
            {
                dx.Add(smTr == 0 ? 0 : Dx(100 * smPlus / smTr, 100 * smMinus / smTr));
            }
        }

        return WilderMean(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(dx), n);
    }

    /// <summary>The beta of a stock to the benchmark: cov(r_i, r_m) / var(r_m) over paired returns. NaN when the benchmark does not vary.</summary>
    public static double Beta(ReadOnlySpan<double> stock, ReadOnlySpan<double> market)
    {
        if (stock.Length != market.Length || stock.Length < 2)
        {
            return double.NaN;
        }

        var ms = Mean(stock);
        var mm = Mean(market);
        double cov = 0, varM = 0;
        for (var i = 0; i < stock.Length; i++)
        {
            cov += (stock[i] - ms) * (market[i] - mm);
            varM += (market[i] - mm) * (market[i] - mm);
        }

        return varM == 0 ? double.NaN : cov / varM;
    }

    /// <summary>The variance ratio <c>var(q-day returns) / (q x var(1-day returns))</c>.</summary>
    public static double VarianceRatio(double varianceQ, double variance1, int q) => variance1 == 0 ? double.NaN : varianceQ / (q * variance1);

    /// <summary>The lag-1 autocorrelation of a series.</summary>
    public static double Autocorrelation1(ReadOnlySpan<double> values)
    {
        if (values.Length < 3)
        {
            return double.NaN;
        }

        var mean = Mean(values);
        double num = 0, den = 0;
        for (var i = 0; i < values.Length; i++)
        {
            den += (values[i] - mean) * (values[i] - mean);
            if (i > 0)
            {
                num += (values[i] - mean) * (values[i - 1] - mean);
            }
        }

        return den == 0 ? double.NaN : num / den;
    }

    /// <summary>The distance <c>sqrt(0.5 x (1 - rho))</c>: 0 for rho 1, 1 for rho -1.</summary>
    public static double CorrelationDistance(double rho) => Math.Sqrt(0.5 * (1 - Math.Clamp(rho, -1, 1)));

    /// <summary>A copy with values below the p-quantile and above the (1 - p)-quantile pulled in to them.</summary>
    public static double[] Winsorise(ReadOnlySpan<double> values, double p)
    {
        var sorted = values.ToArray();
        Array.Sort(sorted);
        var lo = Quantile(sorted, p);
        var hi = Quantile(sorted, 1 - p);
        var result = values.ToArray();
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = Math.Clamp(result[i], lo, hi);
        }

        return result;
    }

    /// <summary>The Spearman correlation: Pearson on average ranks. NaN when a series is constant.</summary>
    public static double Spearman(ReadOnlySpan<double> a, ReadOnlySpan<double> b) => Pearson(Ranks(a), Ranks(b));

    /// <summary>1-based ranks, ties share the average rank.</summary>
    public static double[] Ranks(ReadOnlySpan<double> values)
    {
        var order = Enumerable.Range(0, values.Length).ToArray();
        var copy = values.ToArray();
        Array.Sort(order, (x, y) => copy[x].CompareTo(copy[y]));
        var ranks = new double[copy.Length];
        for (var i = 0; i < order.Length;)
        {
            var j = i;
            while (j + 1 < order.Length && copy[order[j + 1]] == copy[order[i]])
            {
                j++;
            }

            for (var t = i; t <= j; t++)
            {
                ranks[order[t]] = ((i + j) / 2.0) + 1;
            }

            i = j + 1;
        }

        return ranks;
    }

    public static double Pearson(ReadOnlySpan<double> a, ReadOnlySpan<double> b)
    {
        if (a.Length != b.Length || a.Length < 2)
        {
            return double.NaN;
        }

        var ma = Mean(a);
        var mb = Mean(b);
        double cov = 0, va = 0, vb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            cov += (a[i] - ma) * (b[i] - mb);
            va += (a[i] - ma) * (a[i] - ma);
            vb += (b[i] - mb) * (b[i] - mb);
        }

        return va == 0 || vb == 0 ? double.NaN : cov / Math.Sqrt(va * vb);
    }

    /// <summary>The adjusted Rand index of two labelings of the same items (1 identical, about 0 unrelated).</summary>
    public static double AdjustedRandIndex(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        var n = a.Count;
        if (n != b.Count || n < 2)
        {
            return double.NaN;
        }

        var table = new Dictionary<(int, int), int>();
        var rows = new Dictionary<int, int>();
        var cols = new Dictionary<int, int>();
        for (var i = 0; i < n; i++)
        {
            table[(a[i], b[i])] = table.GetValueOrDefault((a[i], b[i])) + 1;
            rows[a[i]] = rows.GetValueOrDefault(a[i]) + 1;
            cols[b[i]] = cols.GetValueOrDefault(b[i]) + 1;
        }

        static double Pairs(int x) => x * (x - 1) / 2.0;
        var index = table.Values.Sum(v => Pairs(v));
        var rowSum = rows.Values.Sum(v => Pairs(v));
        var colSum = cols.Values.Sum(v => Pairs(v));
        var expected = rowSum * colSum / Pairs(n);
        var max = (rowSum + colSum) / 2;
        return max == expected ? 1 : (index - expected) / (max - expected);
    }

    /// <summary>Average-linkage clustering of a symmetric distance matrix (row-major n x n) cut to k clusters; labels run 1..k in order of first appearance, so the same input gives the same labels.</summary>
    public static int[] AverageLinkage(double[] distance, int n, int k)
    {
        var d = (double[])distance.Clone();
        var size = new int[n];
        var active = new bool[n];
        var root = new int[n];
        for (var i = 0; i < n; i++)
        {
            size[i] = 1;
            active[i] = true;
            root[i] = i;
        }

        for (var clusters = n; clusters > Math.Max(k, 1); clusters--)
        {
            int a = -1, b = -1;
            var best = double.PositiveInfinity;
            for (var i = 0; i < n; i++)
            {
                if (!active[i])
                {
                    continue;
                }

                for (var j = i + 1; j < n; j++)
                {
                    if (active[j] && d[(i * n) + j] < best)
                    {
                        best = d[(i * n) + j];
                        a = i;
                        b = j;
                    }
                }
            }

            for (var x = 0; x < n; x++)
            {
                if (!active[x] || x == a || x == b)
                {
                    continue;
                }

                var merged = ((size[a] * d[(a * n) + x]) + (size[b] * d[(b * n) + x])) / (size[a] + size[b]);
                d[(a * n) + x] = merged;
                d[(x * n) + a] = merged;
            }

            size[a] += size[b];
            active[b] = false;
            for (var i = 0; i < n; i++)
            {
                if (root[i] == b)
                {
                    root[i] = a;
                }
            }
        }

        var labels = new int[n];
        var map = new Dictionary<int, int>();
        for (var i = 0; i < n; i++)
        {
            if (!map.TryGetValue(root[i], out var label))
            {
                label = map.Count + 1;
                map[root[i]] = label;
            }

            labels[i] = label;
        }

        return labels;
    }

    private static double Quantile(double[] sorted, double p)
    {
        var position = p * (sorted.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = Math.Min(lower + 1, sorted.Length - 1);
        return sorted[lower] + ((position - lower) * (sorted[upper] - sorted[lower]));
    }
}
