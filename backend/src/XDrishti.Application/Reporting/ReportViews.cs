using System.Globalization;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>Turns the engine's results into the wire views (P8-06): every figure as a state and a number only for a value, the evidence block on every report, and the limits of this slice added to the engine's.</summary>
internal static class ReportViews
{
    private const string SourcesLimit = "Style is that of the stored strategy and the exit is the candidate of the stored comparison the run belongs to; where none is stored the label is not recorded and no report guesses it.";

    private const string UniverseLimit = "Only closed trades are counted; trades still open at the end of the data are left out. A basket run carries its one basket on every trade, so two runs of one strategy over baskets that share an instrument hold the same signal and are refused rather than added together.";

    public static MetricView Metric(Metric metric)
    {
        ArgumentNullException.ThrowIfNull(metric);
        return new MetricView(ReportNames.Name(metric.State), metric.Number, metric.Reason);
    }

    public static IReadOnlyList<GroupPartView> Parts(GroupKey key) =>
        [.. key.Parts.Select(p => new GroupPartView(ReportNames.Name(p.Dimension), p.Value))];

    public static GroupEvidenceView GroupEvidence(EvidenceBlock block) => new(block.N, block.FirstDate, block.LastDate, block.Provisional, [.. block.RunKeys]);

    public static GroupView Group(ReportGroup g) => new(
        g.Key.Text,
        Parts(g.Key),
        g.N,
        g.Wins,
        Metric(g.WinRate),
        Metric(g.AvgWinR),
        Metric(g.AvgLossR),
        Metric(g.ExpectancyR),
        Metric(g.ProfitFactor),
        Metric(g.TotalR),
        Metric(g.MaxDrawdownR),
        Metric(g.LongestLossStreak),
        Metric(g.AvgHoldingSessions),
        Metric(g.ShrunkWinRate),
        Metric(g.WinRateLower),
        Metric(g.ShrunkExpectancyR),
        Metric(g.ExpectancyLowerR),
        GroupEvidence(g.Evidence));

    /// <summary>The whole population when it holds no trade: every figure is <c>empty</c>, never a zero.</summary>
    public static GroupView EmptyGroup(EvidenceBlock evidence)
    {
        var empty = new MetricView("empty", null, "no trades");
        return new GroupView("all", [], 0, 0, empty, empty, empty, empty, empty, empty, empty, empty, empty, empty, empty, empty, empty, GroupEvidence(evidence));
    }

    public static ReportEvidenceView Evidence(ReportEvidence evidence, LoadedReport report)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(report);
        var block = evidence.Population;
        var openLeftOut = report.Runs.Sum(r => r.OpenAtEnd);
        var limits = new List<string>(evidence.Limits) { UniverseLimit, SourcesLimit };
        if (openLeftOut > 0)
        {
            limits.Add(string.Create(CultureInfo.InvariantCulture, $"{openLeftOut} {(openLeftOut == 1 ? "trade" : "trades")} still open at the end of the data in these runs {(openLeftOut == 1 ? "is" : "are")} left out (counted over the whole runs, before any date, symbol or basket filter of this report)."));
        }

        return new ReportEvidenceView(
            block.Basis == ReportBasis.Net ? "net" : "gross",
            block.N,
            block.FirstDate,
            block.LastDate,
            block.Provisional,
            [.. block.RunKeys],
            [.. report.Runs.OrderBy(r => r.RunKey, StringComparer.Ordinal).Select(r => new RunUsedView(r.RunKey, r.StrategyId, r.UniverseKind + ": " + r.UniverseLabel, r.WindowStart, r.WindowEnd, r.Costed, r.CostsSheetId, r.Style, r.Exit, r.ClosedTrades, r.OpenAtEnd))],
            report.DefaultRunSet,
            limits.AsReadOnly(),
            [.. evidence.UnavailableInputs],
            evidence.DuplicateTradesDropped,
            openLeftOut,
            evidence.NetBasisConfirmation?.Text);
    }

    public static GroupsView Groups(ReportGrouping grouping, LoadedReport report) => new(
        [.. grouping.Dimensions.Select(ReportNames.Name)],
        grouping.NonAdditive,
        [.. grouping.Groups.Select(Group)],
        Evidence(grouping.Evidence, report));

    public static PivotView Pivot(ReportPivot pivot, LoadedReport report) => new(
        ReportNames.Name(pivot.RowDimension),
        ReportNames.Name(pivot.ColumnDimension),
        ReportNames.Name(pivot.Metric),
        [.. pivot.Rows],
        [.. pivot.Columns],
        [.. pivot.Cells.Select(c => new PivotCellView(c.Row, c.Column, c.N, Metric(c.Value)))],
        pivot.NonAdditive,
        Evidence(pivot.Evidence, report));

    public static EquityView Equity(ReportEquity equity, LoadedReport report) => new(
        [.. equity.Dimensions.Select(ReportNames.Name)],
        equity.NonAdditive,
        [.. equity.Series.Select(s => new EquitySeriesView(s.Key.Text, Parts(s.Key), s.N, Metric(s.TotalR), [.. s.Points.Select(p => new EquityPointView(p.Date, p.CumulativeR, p.DrawdownR, p.Trades))], GroupEvidence(s.Evidence)))],
        Evidence(equity.Evidence, report));

    public static DistributionView Distribution(ReportDistribution distribution, LoadedReport report) => new(
        distribution.BinWidthR,
        [.. distribution.Dimensions.Select(ReportNames.Name)],
        distribution.NonAdditive,
        [.. distribution.Groups.Select(g => new DistributionGroupView(g.Key.Text, Parts(g.Key), g.N, Metric(g.MeanR), [.. g.Bins.Select(b => new DistributionBinView(b.LowerR, b.UpperR, b.Count))], GroupEvidence(g.Evidence)))],
        Evidence(distribution.Evidence, report));

    public static ReportMaeMfeView MaeMfe(ReportMaeMfe answer, LoadedReport report) => new(
        [.. answer.Dimensions.Select(ReportNames.Name)],
        answer.NonAdditive,
        [.. answer.Groups.Select(g => new MaeMfeGroupView(
            g.Key.Text,
            Parts(g.Key),
            g.N,
            g.ExcursionN,
            Metric(g.WinnersMaeP90),
            g.Grid is null ? null : new MaeGridView([.. g.Grid.MaeEdges], [.. g.Grid.ResultEdges], [.. g.Grid.Counts.Select(row => (IReadOnlyList<int>)[.. row])], g.Grid.BelowCount, g.Grid.AboveCount),
            g.Mfe is null ? null : new MfeHistogramView([.. g.Mfe.Edges], [.. g.Mfe.Counts], g.Mfe.AboveCount),
            [.. g.ReachedMfe.Select(l => new ReachedMfeView(l.AtR, l.Count, l.Share))],
            GroupEvidence(g.Evidence)))],
        Evidence(answer.Evidence, report));

    public static InsightsView Insights(IReadOnlyList<Insight> insights, LoadedReport report) => new(
        [.. insights.Select(i => new InsightView(KindName(i.Kind), ReportNames.Name(i.State), i.Text, [.. i.Cells]))],
        Evidence(report.Population.Evidence, report));

    public static WhatWorksView WhatWorks(WhatWorks answer, LoadedReport report) => new(
        answer.Kind == WhatWorksKind.Ranked ? "ranked" : "nothing_qualifies",
        [.. answer.Dimensions.Select(ReportNames.Name)],
        [.. answer.Ranked.Select(r => new RankedCellView(r.Rank, Group(r.Group), r.LowerBoundPositive))],
        [.. answer.NotRanked.Select(u => new UnrankedCellView(Group(u.Group), u.Reason))],
        answer.CellsLookedAt,
        answer.CellsComparable,
        answer.MinTrades,
        answer.RequiredTrades,
        answer.LargestN,
        answer.PositiveLowerBound,
        answer.ExpectedChancePositives,
        answer.Headline,
        answer.SelectionNote,
        [.. answer.UnavailableDimensions],
        answer.NonAdditive,
        Evidence(answer.Evidence, report));

    public static TradeLineView TradeLine(ReportTradeLine t) => new(t.RunKey, t.TradeKey, t.Strategy, t.Timeframe, t.Side, t.Symbol, [.. t.BasketIds], t.EntryDate, t.ExitDate, t.R, t.SessionsHeld, t.Style, t.Exit, t.Provisional, t.Ordinal);

    private static string KindName(InsightKind kind) => kind switch
    {
        InsightKind.BestStrategy => "best_strategy",
        InsightKind.WeakestStrategy => "weakest_strategy",
        InsightKind.BasketGap => "basket_gap",
        InsightKind.SideBias => "side_bias",
        InsightKind.BestExit => "best_exit",
        InsightKind.Weekday => "weekday",
        InsightKind.Regime => "regime",
        InsightKind.Calibration => "calibration",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
