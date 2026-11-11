namespace XDrishti.Domain.Reporting;

/// <summary>Volatility and drawdown of the basket index (BK-06 m3, m4). Pure; risk-free rate ignored.</summary>
public static class RiskMetrics
{
    /// <summary>Annualised volatility (fraction) over the last w index returns, for each window; w + 1 points are needed.</summary>
    public static IReadOnlyList<VolatilityFigure> Volatility(IndexSeries index, IReadOnlyList<int> windows)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(windows);
        var points = index.Points.Count;
        var returns = Returns(index);
        var result = new List<VolatilityFigure>(windows.Count);
        foreach (var w in windows)
        {
            if (index.Status.State != MetricState.Value)
            {
                result.Add(new VolatilityFigure(w, index.Status));
            }
            else if (w < 2 || points < w + 1)
            {
                result.Add(new VolatilityFigure(w, BasketStats.Short(points, w + 1)));
            }
            else
            {
                result.Add(new VolatilityFigure(w, Metric.Of(Math.Sqrt(BasketStats.TradingDays) * BasketStats.Stdev(returns.AsSpan(returns.Length - w)))));
            }
        }

        return result;
    }

    /// <summary>Maximum drawdown (fraction, negative) and the longest run of days from a peak to the next new peak; an open run counts the days since the last peak.</summary>
    public static DrawdownResult Drawdown(IndexSeries index, int minPoints)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (index.Status.State != MetricState.Value)
        {
            return new DrawdownResult(index.Status, index.Status, false, 0);
        }

        var points = index.Points;
        if (points.Count < minPoints)
        {
            var shortFigure = BasketStats.Short(points.Count, minPoints);
            return new DrawdownResult(shortFigure, shortFigure, false, 0);
        }

        var peak = points[0].Level;
        var peakAt = 0;
        var worst = 0.0;
        var longest = 0;
        for (var i = 1; i < points.Count; i++)
        {
            var level = points[i].Level;
            if (level > peak)
            {
                longest = Math.Max(longest, i - peakAt);
                peak = level;
                peakAt = i;
            }
            else
            {
                worst = Math.Min(worst, (level / peak) - 1.0);
            }
        }

        var since = points.Count - 1 - peakAt;
        longest = Math.Max(longest, since);
        return new DrawdownResult(Metric.Of(worst), Metric.Of(longest), since > 0, since);
    }

    /// <summary>The drawdown of every index point from its running peak (fraction, zero or negative); empty unless the index is sound.</summary>
    public static IReadOnlyList<double> DrawdownSeries(IndexSeries index)
    {
        ArgumentNullException.ThrowIfNull(index);
        var result = new List<double>(index.Points.Count);
        var peak = double.MinValue;
        foreach (var p in index.Points)
        {
            peak = Math.Max(peak, p.Level);
            result.Add((p.Level / peak) - 1.0);
        }

        return result;
    }

    internal static double[] Returns(IndexSeries index)
    {
        var r = new double[Math.Max(0, index.Points.Count - 1)];
        for (var i = 1; i < index.Points.Count; i++)
        {
            r[i - 1] = index.Points[i].Return;
        }

        return r;
    }
}
