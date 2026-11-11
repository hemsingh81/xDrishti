using XDrishti.Domain.Market.Context;
using XDrishti.Domain.Reporting;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Application.Reporting;

/// <summary>Turns the pure <see cref="BasketProfile"/> into the wire view (BK-07): every figure through <see cref="ReportViews.Metric"/>, so a non-value carries no number, and the series aligned on the index dates.</summary>
internal static class BasketProfileMapper
{
    /// <summary>Most members drawn in the correlation matrix.</summary>
    public const int MatrixMembers = 40;

    public const string Basis = "equal weight, daily rebalanced, price return, dividends not included";

    private static MetricView M(Metric metric) => ReportViews.Metric(metric);

    public static ProfileSeriesView Series(BasketProfile profile, IReadOnlyList<DailyClose> benchmark)
    {
        var points = profile.Index.Points;
        var bench = benchmark.Where(c => double.IsFinite(c.Close) && c.Close > 0).GroupBy(c => c.Date).ToDictionary(g => g.Key, g => g.First().Close);
        double? baseClose = points.Count > 0 && bench.TryGetValue(points[0].Date, out var b0) ? b0 : null;
        var rs = profile.RelativeStrength.Line.ToDictionary(p => p.Date, p => p.Value);
        var beta = profile.Benchmark.RollingBeta.ToDictionary(p => p.Date, p => p.Beta);
        return new ProfileSeriesView(
            [.. points.Select(p => p.Date)],
            [.. points.Select(p => p.Level)],
            [.. points.Select(p => baseClose is { } b && bench.TryGetValue(p.Date, out var close) ? (double?)(100.0 * close / b) : null)],
            [.. points.Select(p => rs.TryGetValue(p.Date, out var v) ? (double?)v : null)],
            [.. RiskMetrics.DrawdownSeries(profile.Index)],
            [.. points.Select(p => beta.TryGetValue(p.Date, out var v) ? (double?)v : null)]);
    }

    /// <summary>Why an index-relative figure is unavailable; null when the index has bars. The index is never replaced by another one (BK-06c).</summary>
    public static string? NoBenchmark(ResolvedWindow window, bool hasBars) => window.Index is null
        ? window.IndexMissing ?? "no benchmark index is in the instrument master"
        : hasBars ? null : $"{window.IndexName} has no stored daily bars in this window";

    /// <summary>Without index bars every figure against the index is unavailable, with the reason, instead of the engine's too-few-days state.</summary>
    public static MetricView Against(Metric metric, string? noBenchmark) => noBenchmark is null ? M(metric) : M(Metric.Unavailable(noBenchmark));

    public static ProfileRiskView Risk(BasketProfile profile, BasketProfileOptions options, string? noBenchmark)
    {
        MetricView Vol(int w) => M(profile.Volatility.FirstOrDefault(v => v.Window == w)?.Volatility ?? Metric.Unavailable($"no {w}-session window"));
        BetaFigure? Beta(int w) => profile.Benchmark.Figures.FirstOrDefault(f => f.Window == w);
        var long252 = Beta(options.BetaWindows.Max());
        MetricView Of(BetaFigure? f, Func<BetaFigure, Metric> pick) => Against(f is null ? Metric.Unavailable("no such window") : pick(f), noBenchmark);
        return new ProfileRiskView(
            Vol(20),
            Vol(60),
            Vol(252),
            M(profile.Drawdown.MaxDrawdown),
            M(profile.Drawdown.LongestDays),
            profile.Drawdown.Open,
            profile.Drawdown.DaysSincePeak,
            Of(Beta(60), f => f.Beta),
            Of(long252, f => f.Beta),
            Of(long252, f => f.Alpha),
            Of(long252, f => f.TrackingError),
            Of(long252, f => f.InformationRatio),
            Against(profile.Benchmark.Capture.Up, noBenchmark),
            Against(profile.Benchmark.Capture.Down, noBenchmark));
    }

    public static BreadthFigureView Breadth(BreadthFigure f) => new(M(f.Share), f.Voters, f.Excluded, f.ExcludedReason);

    public static ProfileBreadthView Breadth(BasketBreadthResult b, IReadOnlyList<BreadthPoint> series) => new(
        Breadth(b.AboveSma20),
        Breadth(b.AboveSma50),
        Breadth(b.AboveSma200),
        Breadth(b.AdvanceShare),
        Breadth(b.NewHighShare),
        b.NoBar,
        [.. series.Select(p => p.Date)],
        [.. series.Select(p => p.Share.Number)]);

    public static ProfileContributionView Contribution(ContributionResult c)
    {
        static ContributionRowView Row(ContributionRow r) => new(r.Symbol, r.ReturnPct, r.Share);
        return new ProfileContributionView(M(c.Top3Share), c.Included, [.. c.Top.Select(Row)], [.. c.Bottom.Select(Row)], [.. c.Rows.Select(Row)]);
    }

    public static ProfileRegimeView Regime(RegimeResult r, int minDays) => new(
        M(r.Status),
        r.Unknown,
        minDays,
        [.. r.Rows.Select(x => new RegimeRowView(x.Code, x.Code is >= 1 and <= 9 ? RegimeNames.NameOf(x.Code) : "unknown", x.Days, M(x.MeanDailyReturn), M(x.AnnualVolatility), M(x.HitRate), ReportViews.Metric(x.MeanDailyReturn).State))]);

    public static ProfileCapacityView Capacity(BasketCapacityResult c, double capacityPct) =>
        new(M(c.MedianAdv), M(c.P10Adv), M(c.BasketCapacity), M(c.AsmGsmShare), M(c.FnoShare), c.Counted, c.ShortMembers, capacityPct);

    /// <summary>The matrix of the most liquid members (at most <see cref="MatrixMembers"/>), in sector order and then by symbol; null when there is no correlation figure to draw.</summary>
    public static CorrelationMatrixView? Matrix(IReadOnlyDictionary<string, IReadOnlyList<DailyReturn>> returns, BasketProfile profile, IReadOnlyDictionary<string, string?> sectors, BasketProfileOptions options)
    {
        if (profile.Correlation.Members < options.MinCorrelationMembers || profile.AsOf is not { } asOf)
        {
            return null;
        }

        var adv = profile.Capacity.AdvBySymbol;
        var chosen = returns.Keys.OrderByDescending(s => adv.TryGetValue(s, out var a) ? a : 0.0).ThenBy(s => s, StringComparer.Ordinal).Take(MatrixMembers)
            .OrderBy(s => sectors.GetValueOrDefault(s) ?? "~", StringComparer.Ordinal).ThenBy(s => s, StringComparer.Ordinal).ToList();
        var matrix = PairwiseCorrelation.Matrix(chosen, returns, asOf, options);
        return new CorrelationMatrixView(returns.Count, matrix.Symbols, matrix.Rho);
    }

    public static ProfileHoldOutView HoldOut(IReadOnlyList<HoldOutCell> cells, int trades) => new(
        trades == 0 ? ReportViews.Metric(Metric.Empty("no closed trade of this basket in the default run set")) : ReportViews.Metric(Metric.Of(trades)),
        trades,
        HoldOutStability.MinPerHalf,
        [.. cells.Select(c => new ProfileHoldOutCellView(c.Strategy, c.SplitDate, c.FirstN, c.SecondN, M(c.FirstExpectancy), M(c.SecondExpectancy), c.Verdict))]);

    public static ProfileHoldOutView HoldOutUnavailable(string reason) => new(ReportViews.Metric(Metric.Unavailable(reason)), 0, HoldOutStability.MinPerHalf, []);
}
