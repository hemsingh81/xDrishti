using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// Beta, alpha, tracking error, information ratio and up/down capture of the basket index against the benchmark (BK-06 m5 to m7). One-factor OLS, no lag, risk-free rate ignored (Decisions 5 and 9). A zero variance gives not evaluable, never infinity.
/// </summary>
public static class BenchmarkRegression
{
    private const double Flat = 1e-18;

    public static BenchmarkRelation Compute(IndexSeries index, IReadOnlyList<DailyReturn> benchmark, BasketProfileOptions options)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(benchmark);
        ArgumentNullException.ThrowIfNull(options);
        if (index.Status.State != MetricState.Value)
        {
            var none = new BetaFigure(0, index.Status, index.Status, index.Status, index.Status);
            return new BenchmarkRelation([.. options.BetaWindows.Select(w => none with { Window = w })], [], new CaptureResult(index.Status, index.Status), 0);
        }

        var (dates, b, m) = Align(index, benchmark);
        var figures = new List<BetaFigure>(options.BetaWindows.Count);
        foreach (var w in options.BetaWindows)
        {
            figures.Add(dates.Length < w || w < 3 ? Short(w, dates.Length) : Window(w, b.AsSpan(b.Length - w), m.AsSpan(m.Length - w)));
        }

        var rolling = new List<RollingBetaPoint>();
        var rw = options.RollingBetaWindow;
        for (var end = rw; rw >= 3 && end <= dates.Length; end++)
        {
            var bs = b.AsSpan(end - rw, rw);
            var ms = m.AsSpan(end - rw, rw);
            var variance = BasketStats.Variance(ms);
            if (variance > Flat)
            {
                rolling.Add(new RollingBetaPoint(dates[end - 1], BasketStats.Covariance(bs, ms) / variance));
            }
        }

        return new BenchmarkRelation(figures, rolling, Capture(b, m, options.MinCaptureDays), dates.Length);
    }

    private static BetaFigure Short(int w, int n)
    {
        var s = BasketStats.Short(n, w);
        return new BetaFigure(w, s, s, s, s);
    }

    private static BetaFigure Window(int w, ReadOnlySpan<double> b, ReadOnlySpan<double> m)
    {
        var variance = BasketStats.Variance(m);
        var active = new double[w];
        for (var i = 0; i < w; i++)
        {
            active[i] = b[i] - m[i];
        }

        var te = Math.Sqrt(BasketStats.TradingDays) * BasketStats.Stdev(active);
        var teMetric = Metric.Of(te);
        var ir = te > Math.Sqrt(Flat) ? Metric.Of(BasketStats.TradingDays * BasketStats.Mean(active) / te) : Metric.NotEvaluable("the tracking error is zero");
        if (!(variance > Flat))
        {
            const string Why = "the index does not move in the window, so beta is undefined";
            return new BetaFigure(w, Metric.NotEvaluable(Why), Metric.NotEvaluable(Why), teMetric, Metric.NotEvaluable(Why));
        }

        var beta = BasketStats.Covariance(b, m) / variance;
        return new BetaFigure(w, Metric.Of(beta), Metric.Of(BasketStats.TradingDays * (BasketStats.Mean(b) - (beta * BasketStats.Mean(m)))), teMetric, ir);
    }

    private static CaptureResult Capture(double[] b, double[] m, int min)
    {
        double upB = 0, upM = 0, dnB = 0, dnM = 0;
        int up = 0, dn = 0;
        for (var i = 0; i < m.Length; i++)
        {
            if (m[i] > 0)
            {
                upB += b[i];
                upM += m[i];
                up++;
            }
            else if (m[i] < 0)
            {
                dnB += b[i];
                dnM += m[i];
                dn++;
            }
        }

        return new CaptureResult(
            up < min ? Metric.Insufficient($"{up} up days, at least {min} needed") : Metric.Of(upB / upM),
            dn < min ? Metric.Insufficient($"{dn} down days, at least {min} needed") : Metric.Of(dnB / dnM));
    }

    private static (DateOnly[] Dates, double[] Basket, double[] Benchmark) Align(IndexSeries index, IReadOnlyList<DailyReturn> benchmark)
    {
        var byDate = new Dictionary<DateOnly, double>(benchmark.Count);
        foreach (var r in benchmark)
        {
            byDate[r.Date] = r.Value;
        }

        var dates = new List<DateOnly>();
        var basket = new List<double>();
        var bench = new List<double>();
        for (var i = 1; i < index.Points.Count; i++)
        {
            if (byDate.TryGetValue(index.Points[i].Date, out var rm))
            {
                dates.Add(index.Points[i].Date);
                basket.Add(index.Points[i].Return);
                bench.Add(rm);
            }
        }

        return ([.. dates], [.. basket], [.. bench]);
    }
}
