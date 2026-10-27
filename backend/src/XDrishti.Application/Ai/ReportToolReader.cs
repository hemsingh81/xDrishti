using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;
using XDrishti.Domain.Assistant;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Ai;

/// <summary>
/// The report engine for the assistant's <c>get_report</c> and <c>list_runs</c> (spec P9-05 AC 2 and 3, ADR 0139): it calls the very handlers of the Reports screen and only reshapes their views into rows, data and evidence. It computes nothing itself. Gross basis only: a request for net is
/// answered <c>reports.net_not_confirmed</c> in words, because the owner's confirmation lives in settings no assistant role may read. The engine's own refusals (<c>reports.*</c>) pass through with their code.
/// </summary>
internal sealed class ReportToolReader(IServiceScopeFactory scopes) : IAiReportReader
{
    private const string NetWords = "net is not offered by the assistant yet; the report is gross only";

    private static readonly AiToolColumn[] GroupColumns =
        [new("key", false), new("n", false), new("wins", false), new("winRate", false), new("avgWinR", false), new("avgLossR", false), new("expectancyR", false), new("profitFactor", false), new("totalR", false), new("maxDrawdownR", false), new("winRateLower", false), new("expectancyLowerR", false)];

    public async Task<AiToolData> GetReportAsync(string report, IReadOnlyDictionary<string, string> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.TryGetValue("basis", out var basis) && !string.Equals(basis, "gross", StringComparison.Ordinal))
        {
            return AiToolData.Fail("reports.net_not_confirmed", NetWords);
        }

        var parsed = ReportQuery.Parse(new ReportQueryInput(
            Get(query, "runs"), "gross", Get(query, "lens"), Get(query, "strategy"), Get(query, "timeframe"), Get(query, "side"), Get(query, "style"), Get(query, "exit"), Get(query, "basket"), Get(query, "symbol"), Get(query, "from"), Get(query, "to"), Get(query, "metric")));
        if (parsed.IsFailure)
        {
            return Fail(parsed.Error);
        }

        var q = parsed.Value;
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        switch (report)
        {
            case "summary":
                return await RunAsync<GetReportSummaryQuery, SummaryView>(sp, new GetReportSummaryQuery(q), v => Summary(v), cancellationToken);
            case "groups":
                return await RunAsync<GetReportGroupsQuery, GroupsView>(sp, new GetReportGroupsQuery(q), v => GroupsData(v), cancellationToken);
            case "pivot":
                return await RunAsync<GetReportPivotQuery, PivotView>(sp, new GetReportPivotQuery(q), v => Pivot(v), cancellationToken);
            case "what_works":
                return await RunAsync<GetReportWhatWorksQuery, WhatWorksView>(sp, new GetReportWhatWorksQuery(q), v => WhatWorks(v), cancellationToken);
            case "equity":
                return await RunAsync<GetReportEquityQuery, EquityView>(sp, new GetReportEquityQuery(q), v => Equity(v), cancellationToken);
            case "distribution":
                return await RunAsync<GetReportDistributionQuery, DistributionView>(sp, new GetReportDistributionQuery(q), v => Distribution(v), cancellationToken);
            case "insights":
                return await RunAsync<GetReportInsightsQuery, InsightsView>(sp, new GetReportInsightsQuery(q), v => Insights(v), cancellationToken);
            default:
                return AiToolData.Fail(ToolErrorCodes.InvalidArguments, "Unknown report kind.");
        }
    }

    public async Task<AiToolData> ListSimRunsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<ListReportRunsQuery, Result<RunsView>>>();
        var result = await handler.HandleAsync(new ListReportRunsQuery(), cancellationToken);
        if (result.IsFailure)
        {
            return Fail(result.Error);
        }

        var view = result.Value;
        AiToolColumn[] columns = [new("runKey", false), new("strategyId", false), new("window", false), new("costed", false), new("universe", true), new("labels", true), new("isExperimentCandidate", false), new("inDefaultSet", false), new("leftOutReason", true)];
        var rows = view.Runs.Select(r => (IReadOnlyList<object?>)
        [
            r.RunKey, r.StrategyId, $"{r.WindowStart:yyyy-MM-dd}..{r.WindowEnd:yyyy-MM-dd}", r.Costed, $"{r.UniverseKind}: {r.UniverseLabel}",
            $"style={r.Style ?? "not recorded"}; exit={r.Exit ?? "not recorded"}", r.DefaultSetNote is not null && r.DefaultSetNote.Contains("experiment", StringComparison.OrdinalIgnoreCase), r.InDefaultSet, r.DefaultSetNote,
        ]).ToList();
        return AiToolData.OfRows(
            columns,
            rows,
            new ToolEvidence("stored simulation runs (gross R unless costed)", [], null, view.Runs.Count, ["the default run set is the newest run of each strategy and universe", "net figures are not offered by the assistant"], []),
            [view.Net.Reason ?? "net figures are not offered by the assistant yet", view.DefaultSetRule]);
    }

    private static string? Get(IReadOnlyDictionary<string, string> query, string key) => query.TryGetValue(key, out var v) ? v : null;

    private static AiToolData Fail(Error error) => AiToolData.Fail(error.Code, error.Message);

    private static async Task<AiToolData> RunAsync<TQuery, TView>(IServiceProvider sp, TQuery query, Func<TView, AiToolData> shape, CancellationToken cancellationToken)
        where TQuery : IQuery<Result<TView>>
    {
        var handler = sp.GetRequiredService<IQueryHandler<TQuery, Result<TView>>>();
        var result = await handler.HandleAsync(query, cancellationToken);
        return result.IsFailure ? Fail(result.Error) : shape(result.Value);
    }

    private static ToolEvidence Evidence(ReportEvidenceView e)
    {
        var population = e.Runs.Select(r => $"{r.RunKey} {r.StrategyId} {Plain(r.Universe)} {r.WindowStart:yyyy-MM-dd}..{r.WindowEnd:yyyy-MM-dd} {(r.Costed ? "costed" : "gross")} closed={r.ClosedTrades.ToString(CultureInfo.InvariantCulture)}").ToList();
        var range = e.FirstDate is { } f && e.LastDate is { } l ? $"{f:yyyy-MM-dd}..{l:yyyy-MM-dd}" : null;
        var unavailable = e.UnavailableInputs.Select(u => new ToolUnavailable("input", u)).ToList();
        var limits = new List<string>(e.Limits);
        if (e.DuplicateTradesDropped > 0)
        {
            limits.Add($"{e.DuplicateTradesDropped} repeated record(s) of a trade were counted once");
        }

        if (e.Provisional)
        {
            limits.Add("a contributing run is provisional");
        }

        return new ToolEvidence(e.Basis, population, range, e.N, limits, unavailable);
    }

    /// <summary>A label that came from the database (a basket name inside a universe label) is escaped and scrubbed before it enters the evidence; the run key beside it is not touched.</summary>
    private static string Plain(string label) => (string)CellCleaner.Clean(label, true, new CleanCounters())!;

    private static JsonObject Metric(MetricView m)
    {
        var node = new JsonObject { ["state"] = m.State };
        if (m.Number is { } n)
        {
            node["number"] = n;
        }

        if (m.Reason is not null)
        {
            node["reason"] = m.Reason;
        }

        return node;
    }

    private static List<object?> GroupRow(GroupView g) =>
        [g.Key, g.N, g.Wins, Metric(g.WinRate), Metric(g.AvgWinR), Metric(g.AvgLossR), Metric(g.ExpectancyR), Metric(g.ProfitFactor), Metric(g.TotalR), Metric(g.MaxDrawdownR), Metric(g.WinRateLower), Metric(g.ExpectancyLowerR)];

    private static AiToolData Summary(SummaryView v) =>
        AiToolData.OfRows(GroupColumns, [GroupRow(v.All)], Evidence(v.Evidence));

    private static AiToolData GroupsData(GroupsView v) =>
        AiToolData.OfRows(
            GroupColumns,
            [.. v.Groups.Select(g => (IReadOnlyList<object?>)GroupRow(g))],
            Evidence(v.Evidence),
            v.NonAdditive ? ["a trade can sit in more than one group, so the groups must not be added together"] : [],
            new JsonObject { ["dimensions"] = new JsonArray([.. v.Dimensions.Select(d => (JsonNode?)d)]), ["nonAdditive"] = v.NonAdditive });

    private static AiToolData Pivot(PivotView v) =>
        AiToolData.OfRows(
            [new("row", false), new("column", false), new("n", false), new("value", false)],
            [.. v.Cells.Select(c => (IReadOnlyList<object?>)[c.Row, c.Column, c.N, Metric(c.Value)])],
            Evidence(v.Evidence),
            v.NonAdditive ? ["a trade can sit in more than one cell, so the cells must not be added together"] : [],
            new JsonObject { ["rowDimension"] = v.RowDimension, ["columnDimension"] = v.ColumnDimension, ["metric"] = v.Metric, ["nonAdditive"] = v.NonAdditive });

    private static AiToolData WhatWorks(WhatWorksView v) =>
        AiToolData.OfRows(
            [new("rank", false), .. GroupColumns, new("lowerBoundPositive", false)],
            [.. v.Ranked.Select(r => (IReadOnlyList<object?>)[r.Rank, .. GroupRow(r.Group), r.LowerBoundPositive])],
            Evidence(v.Evidence),
            [v.Headline, v.SelectionNote],
            new JsonObject
            {
                ["kind"] = v.Kind,
                ["cellsLookedAt"] = v.CellsLookedAt,
                ["cellsComparable"] = v.CellsComparable,
                ["minTrades"] = v.MinTrades,
                ["requiredTrades"] = v.RequiredTrades,
                ["largestN"] = v.LargestN,
                ["positiveLowerBound"] = v.PositiveLowerBound,
                ["expectedChancePositives"] = v.ExpectedChancePositives,
                ["notRanked"] = v.NotRanked.Count,
            });

    private static AiToolData Equity(EquityView v) =>
        AiToolData.OfRows(
            [new("key", false), new("n", false), new("totalR", false), new("lastCumulativeR", false), new("deepestDrawdownR", false), new("points", false)],
            [.. v.Series.Select(s => (IReadOnlyList<object?>)[s.Key, s.N, Metric(s.TotalR), s.Points.Count > 0 ? s.Points[^1].CumulativeR : null, s.Points.Count > 0 ? s.Points.Min(p => p.DrawdownR) : null, s.Points.Count])],
            Evidence(v.Evidence),
            ["the daily points of each curve are on the Reports screen; this answer gives the end of each curve"]);

    private static AiToolData Distribution(DistributionView v) =>
        AiToolData.OfRows(
            [new("key", false), new("n", false), new("meanR", false), new("bins", false)],
            [.. v.Groups.Select(g => (IReadOnlyList<object?>)[g.Key, g.N, Metric(g.MeanR), g.Bins.Count])],
            Evidence(v.Evidence),
            [$"bins are {v.BinWidthR.ToString(CultureInfo.InvariantCulture)} R wide; the bin counts are on the Reports screen"]);

    private static AiToolData Insights(InsightsView v) =>
        AiToolData.OfRows(
            [new("kind", false), new("state", false), new("text", false), new("cells", false)],
            [.. v.Insights.Select(i => (IReadOnlyList<object?>)[i.Kind, i.State, i.Text, i.Cells])],
            Evidence(v.Evidence));
}
