namespace XDrishti.Application.Portfolio.Returns;

/// <summary>Maximum drawdown of a NAV series (ADR 0020 §4). Pure: one pass keeping the highest NAV so far.</summary>
public static class DrawdownCalculator
{
    private const int Decimals = 4;

    /// <summary>The largest fall from a peak to a later low among the dated NAVs (in date order); null for no points.</summary>
    public static Drawdown? Max(IReadOnlyList<(DateOnly Date, decimal Nav)> series)
    {
        ArgumentNullException.ThrowIfNull(series);
        if (series.Count == 0)
        {
            return null;
        }

        var peak = series[0].Nav;
        var peakDate = series[0].Date;
        var worst = 0m;
        DateOnly? from = null;
        DateOnly? to = null;
        foreach (var (date, nav) in series)
        {
            if (nav > peak)
            {
                peak = nav;
                peakDate = date;
                continue;
            }

            if (peak <= 0m)
            {
                continue;
            }

            var fall = (peak - nav) / peak;
            if (fall > worst)
            {
                worst = fall;
                from = peakDate;
                to = date;
            }
        }

        return worst == 0m ? new Drawdown(0m, null, null) : new Drawdown(Math.Round(worst * 100m, Decimals, MidpointRounding.AwayFromZero), from, to);
    }

    /// <summary>The drawdown of the portfolio's NAV in the points.</summary>
    public static Drawdown? OfPortfolio(IReadOnlyList<HistoryPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        return Max([.. points.Select(p => (p.Date, p.Nav))]);
    }

    /// <summary>The drawdown of the benchmark's NAV in the points that have one (it ends at the index's last close); null when no point carries a benchmark.</summary>
    public static Drawdown? OfBenchmark(IReadOnlyList<HistoryPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        var valued = points.Where(p => p.BenchmarkNav is not null).Select(p => (p.Date, p.BenchmarkNav!.Value)).ToArray();
        return valued.Length == 0 ? null : Max(valued);
    }
}
