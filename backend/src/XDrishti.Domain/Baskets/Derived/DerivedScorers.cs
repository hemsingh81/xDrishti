namespace XDrishti.Domain.Baskets.Derived;

/// <summary>What a scorer says about one instrument: a named exclusion, or a score and whether it passes the basket's gate.</summary>
internal readonly record struct Outcome(string? Exclusion, double Score, bool Passes, double Secondary = 0)
{
    public static Outcome Insufficient => new(ExclusionReason.InsufficientHistory, 0, false);

    public static Outcome Of(double score, bool passes, double secondary = 0) => new(null, score, passes, secondary);
}

/// <summary>The inputs of one scoring: parameters, the as-of date, the formation time and the daily bar count up to as_of.</summary>
internal sealed record ScoreContext(DerivedParams P, DateOnly AsOf, DateTimeOffset? Formation);

/// <summary>The per-basket formulas (spec BK-02 table). Pure: each reads only daily bars up to <c>n</c> and intraday bars before the cutoff.</summary>
internal static class DerivedScorers
{
    private const double Eps = 1e-12;

    private const int FirstMinutes = DerivedSchedule.GapperMinutes;

    public static Outcome Score(int id, InstrumentBars i, int n, ScoreContext c)
    {
        var d = i.Daily.AsSpan(0, n);
        var p = c.P;
        return id switch
        {
            1 => VolatilityAtr(d, p),
            2 => MostMoving(d, p),
            3 => Liquid(d, p),
            4 => Gapper(i, d, c),
            5 => HighBeta(i, n, c),
            6 => Momentum(d, p),
            7 => MeanReverting(d, p),
            8 => Trending(d, p),
            9 => NearHigh(d, p),
            10 => LowVolatility(d, p),
            11 => RelativeVolume(i, d, c),
            12 => OpeningRange(i, c),
            13 => IntradayTradable(i, d, c),
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        };
    }

    public static bool Ge(double a, double b) => a >= b - Eps;

    private static bool Gt(double a, double b) => a > b + Eps;

    private static bool Lt(double a, double b) => a < b - Eps;

    private static Outcome VolatilityAtr(ReadOnlySpan<Bar> d, DerivedParams p)
    {
        var atr = IndicatorKit.Atr(d, p.Whole("Basket1.AtrWindow"));
        return Outcome.Of(atr / d[^1].Close, true);
    }

    private static Outcome MostMoving(ReadOnlySpan<Bar> d, DerivedParams p)
    {
        var w = p.Whole("Basket2.Window");
        var values = new double[w];
        if (p.Whole("Basket2.Variant") == 1)
        {
            for (var k = 0; k < w; k++)
            {
                var b = d[d.Length - w + k];
                values[k] = (b.High - b.Low) / b.Close;
            }
        }
        else
        {
            var returns = IndicatorKit.Returns(d, w);
            for (var k = 0; k < returns.Length; k++)
            {
                values[k] = Math.Abs(returns[k]);
            }
        }

        return Outcome.Of(IndicatorKit.Mean(values), true);
    }

    private static Outcome Liquid(ReadOnlySpan<Bar> d, DerivedParams p) => Outcome.Of(IndicatorKit.MedianTradedValue(d, p.Whole("Basket3.Window")), true);

    private static Outcome Gapper(InstrumentBars i, ReadOnlySpan<Bar> d, ScoreContext c)
    {
        var p = c.P;
        var sessions = IntradaySessions.Build(i.Intraday, c.AsOf, c.Formation);
        var todayDate = IntradaySessions.DateOf(c.Formation!.Value);
        var today = sessions.Find(s => s.Date == todayDate && s.Date > c.AsOf);
        var prior = sessions.Where(s => s.Date <= c.AsOf && s.IsComplete(0, FirstMinutes)).TakeLast(p.Whole("Basket4.VolWindow")).ToList(); // a prior session with a missing opening minute does not vote
        if (today is null || !today.IsComplete(0, FirstMinutes) || prior.Count == 0)
        {
            return Outcome.Insufficient;
        }

        var previousClose = d[^1].Close;
        var gap = (today.Bars[0].Open / previousClose) - 1;
        var atrPct = IndicatorKit.Atr(d, p.Whole("Basket4.AtrWindow")) / previousClose;
        var bar = Math.Max(p["Basket4.GapFloor"], p["Basket4.AtrMult"] * atrPct);
        var firstFive = today.Between(0, FirstMinutes).Sum(b => b.Volume);
        var median = IndicatorKit.Median(prior.Select(s => s.Between(0, FirstMinutes).Sum(b => b.Volume)).ToArray());
        return Outcome.Of(Math.Abs(gap), Ge(Math.Abs(gap), bar) && Ge(firstFive, median));
    }

    private static Outcome HighBeta(InstrumentBars i, int n, ScoreContext c)
    {
        var p = c.P;
        var (stock, market) = Aligned(i, n, c.AsOf, p.Whole("Basket5.Window"));
        if (stock.Length < p.Whole("Basket5.MinBars") - 1)
        {
            return Outcome.Insufficient;
        }

        var beta = IndicatorKit.Beta(stock, market);
        return Outcome.Of(beta, Gt(beta, p["Basket5.MinBeta"]));
    }

    private static Outcome Momentum(ReadOnlySpan<Bar> d, DerivedParams p)
    {
        var skip = p.Whole("Basket6.Skip");
        var variant = p.Whole("Basket6.Variant");
        var longWindow = variant == 1 ? p.Whole("Basket6.ShortWindow") : p.Whole("Basket6.LongWindow");
        var score = (d[d.Length - skip].Close / d[d.Length - longWindow].Close) - 1;
        if (variant == 2)
        {
            score /= IndicatorKit.RealisedVolatility(IndicatorKit.Returns(d, p.Whole("Basket6.LongWindow")));
        }

        return Outcome.Of(score, true);
    }

    private static Outcome MeanReverting(ReadOnlySpan<Bar> d, DerivedParams p)
    {
        var lag = p.Whole("Basket7.Lag");
        var span = d[Math.Max(0, d.Length - p.Whole("Basket7.Window") - 1)..];
        var r1 = IndicatorKit.Returns(span, span.Length);
        var rq = new double[span.Length - lag];
        for (var t = lag; t < span.Length; t++)
        {
            rq[t - lag] = (span[t].Close / span[t - lag].Close) - 1;
        }

        var vr = IndicatorKit.VarianceRatio(IndicatorKit.Variance(rq), IndicatorKit.Variance(r1), lag);
        var rho = IndicatorKit.Autocorrelation1(r1);
        var er = IndicatorKit.EfficiencyRatio(d, p.Whole("Basket7.ErWindow"));
        return Outcome.Of(vr, Lt(vr, p["Basket7.MaxVr"]) && Lt(rho, p["Basket7.MaxRho1"]) && Lt(er, p["Basket7.MaxEr"]));
    }

    private static Outcome Trending(ReadOnlySpan<Bar> d, DerivedParams p)
    {
        var er = IndicatorKit.EfficiencyRatio(d, p.Whole("Basket8.ErWindow"));
        var adx = IndicatorKit.Adx(d, p.Whole("Basket8.AdxWindow"));
        return Outcome.Of(er, Gt(er, p["Basket8.MinEr"]) && Gt(adx, p["Basket8.MinAdx"]));
    }

    private static Outcome NearHigh(ReadOnlySpan<Bar> d, DerivedParams p)
    {
        var window = p.Whole("Basket9.Window");
        var high = double.MinValue;
        for (var k = d.Length - window; k < d.Length; k++)
        {
            high = Math.Max(high, d[k].High);
        }

        var sma = 0.0;
        var smaWindow = p.Whole("Basket9.SmaWindow");
        for (var k = d.Length - smaWindow; k < d.Length; k++)
        {
            sma += d[k].Close;
        }

        sma /= smaWindow;
        var ratio = d[^1].Close / high;
        var adtv = IndicatorKit.MedianTradedValue(d, 20);
        return Outcome.Of(ratio, Ge(ratio, p["Basket9.MinRatio"]) && Ge(adtv, p["Basket9.AdtvFloor"]) && d[^1].Close > sma);
    }

    private static Outcome LowVolatility(ReadOnlySpan<Bar> d, DerivedParams p) =>
        Outcome.Of(IndicatorKit.RealisedVolatility(IndicatorKit.Returns(d, p.Whole("Basket10.Window"))), true, IndicatorKit.MedianTradedValue(d, p.Whole("Basket10.AdtvWindow")));

    private static Outcome RelativeVolume(InstrumentBars i, ReadOnlySpan<Bar> d, ScoreContext c)
    {
        var p = c.P;
        var window = p.Whole("Basket11.Window");
        double rvol;
        if (c.Formation is { } formation && i.Intraday is not null)
        {
            var sessions = IntradaySessions.Build(i.Intraday, c.AsOf, formation);
            var todayDate = IntradaySessions.DateOf(formation);
            var today = sessions.Find(s => s.Date == todayDate && s.Date > c.AsOf);
            var prior = sessions.Where(s => s.Date <= c.AsOf).TakeLast(window).ToList();
            if (today is null || prior.Count == 0)
            {
                return Outcome.Insufficient;
            }

            var slot = (int)(formation.ToOffset(IntradaySessions.Ist).TimeOfDay - new TimeSpan(9, 15, 0)).TotalMinutes;
            var median = IndicatorKit.Median(prior.Select(s => s.Between(0, slot).Sum(b => b.Volume)).ToArray());
            rvol = today.Between(0, slot).Sum(b => b.Volume) / median;
        }
        else
        {
            var volumes = new double[window];
            for (var k = 0; k < window; k++)
            {
                volumes[k] = d[d.Length - 1 - window + k].Volume;
            }

            rvol = d[^1].Volume / IndicatorKit.Median(volumes);
        }

        return Outcome.Of(rvol, Ge(rvol, p["Basket11.MinRvol"]) && Ge(d[^1].Close, p["Basket11.MinPrice"]));
    }

    private static Outcome OpeningRange(InstrumentBars i, ScoreContext c)
    {
        var p = c.P;
        var minutes = p.Whole("Basket12.OrMinutes");
        var sessions = IntradaySessions.Build(i.Intraday, c.AsOf, c.Formation);
        var todayDate = IntradaySessions.DateOf(c.Formation!.Value);
        var today = sessions.Find(s => s.Date == todayDate && s.Date > c.AsOf);
        var prior = sessions.Where(s => s.Date <= c.AsOf).TakeLast(p.Whole("Basket12.Window")).Select(s => RangePct(s, minutes)).Where(double.IsFinite).ToArray();
        var now = today is null ? double.NaN : RangePct(today, minutes);
        if (!double.IsFinite(now) || prior.Length < p.Whole("Basket12.MinBars"))
        {
            return Outcome.Insufficient;
        }

        return Outcome.Of(now, Ge(now, p["Basket12.WideMult"] * IndicatorKit.Median(prior)) && Ge(now, p["Basket12.MinOrPct"]));
    }

    private static double RangePct(Session s, int minutes)
    {
        if (!s.IsComplete(0, minutes))
        {
            return double.NaN; // an incomplete window is Insufficient, never a range of whatever minutes happen to be there
        }

        var bars = s.Between(0, minutes).ToArray();
        return bars.Length == 0 ? double.NaN : (bars.Max(b => b.High) - bars.Min(b => b.Low)) / bars[0].Open;
    }

    private static Outcome IntradayTradable(InstrumentBars i, ReadOnlySpan<Bar> d, ScoreContext c)
    {
        var p = c.P;
        var sessions = IntradaySessions.Build(i.Intraday, c.AsOf, c.Formation).Where(s => s.Date <= c.AsOf).TakeLast(p.Whole("Basket13.Window")).ToList();
        var adtv = IndicatorKit.MedianTradedValue(d, 20);
        if (sessions.Count < p.Whole("Basket13.MinBars") || !double.IsFinite(adtv))
        {
            return Outcome.Insufficient;
        }

        var all = sessions.SelectMany(s => s.Bars).ToArray();
        var zeros = all.Count(b => b.Volume == 0);
        var medianRange = IndicatorKit.Median(all.Select(b => (b.High - b.Low) / b.Close).ToArray());
        var pass = Ge(adtv, p["Basket13.MinAdtv"])
            && zeros <= MaxZeroVolumeBars(all.Length, p["Basket13.MaxZeroVolFraction"])
            && Ge(medianRange, p["Basket13.TickMult"] * i.TickSize / d[^1].Close);
        return Outcome.Of(adtv, pass);
    }

    /// <summary>The most zero-volume 1-minute bars allowed: floor(fraction x bars); 18 of a 375-bar session.</summary>
    public static int MaxZeroVolumeBars(int bars, double fraction) => (int)Math.Floor((fraction * bars) + Eps);

    /// <summary>Daily returns of the instrument and the benchmark on the benchmark's last <paramref name="window"/> + 1 dates, for the date pairs where the instrument has both closes.</summary>
    public static (double[] Stock, double[] Market) Aligned(InstrumentBars i, int n, DateOnly asOf, int window)
    {
        var bench = i.Benchmark;
        if (bench is null || n == 0)
        {
            return ([], []);
        }

        var count = 0;
        while (count < bench.Length && bench[count].Date <= asOf)
        {
            count++;
        }

        var closes = new Dictionary<DateOnly, double>(n);
        for (var k = 0; k < n; k++)
        {
            closes[i.Daily[k].Date] = i.Daily[k].Close;
        }

        var stock = new List<double>();
        var market = new List<double>();
        for (var k = Math.Max(1, count - window); k < count; k++)
        {
            if (closes.TryGetValue(bench[k].Date, out var now) && closes.TryGetValue(bench[k - 1].Date, out var before))
            {
                stock.Add((now / before) - 1);
                market.Add((bench[k].Close / bench[k - 1].Close) - 1);
            }
        }

        return ([.. stock], [.. market]);
    }
}
