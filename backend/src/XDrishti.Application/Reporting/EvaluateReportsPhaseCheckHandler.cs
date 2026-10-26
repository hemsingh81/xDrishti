using System.Globalization;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>
/// The P8 phase check (spec P8-17, ADR 0126): calls the built report handlers in process on the default set (the answer path twice, cold and warm; every report twice so that determinism is a repeat read), scans their answers and the route table, and hands the
/// facts to the pure <see cref="ReportsPhaseCheck"/>. A read: nothing is written, no job runs, no setting changes. One check at a time; the loads it makes are the ones the screens make, bounded by the report gate and the closed-trade limit. A report that
/// refuses to load is NOT EVALUABLE, never a pass. The interaction test, the stopwatch note, the bundle and the browser pass are host files this handler cannot read (they are null here).
/// </summary>
internal sealed class EvaluateReportsPhaseCheckHandler(
    IQueryHandler<ListReportRunsQuery, Result<RunsView>> runs,
    IQueryHandler<GetReportSummaryQuery, Result<SummaryView>> summary,
    IQueryHandler<GetReportGroupsQuery, Result<GroupsView>> groups,
    IQueryHandler<GetReportPivotQuery, Result<PivotView>> pivot,
    IQueryHandler<GetReportEquityQuery, Result<EquityView>> equity,
    IQueryHandler<GetReportDistributionQuery, Result<DistributionView>> distribution,
    IQueryHandler<GetReportMaeMfeQuery, Result<ReportMaeMfeView>> maeMfe,
    IQueryHandler<GetReportInsightsQuery, Result<InsightsView>> insights,
    IQueryHandler<GetReportWhatWorksQuery, Result<WhatWorksView>> whatWorks,
    IQueryHandler<PageReportTradesQuery, Result<TradesView>> trades,
    IEndpointCatalogue catalogue,
    TimeProvider time,
    ILogger<EvaluateReportsPhaseCheckHandler> logger) : IQueryHandler<EvaluateReportsPhaseCheckQuery, Result<ReportsPhaseCheckView>>
{
    private static readonly SemaphoreSlim Flight = new(1, 1);

    public async ValueTask<Result<ReportsPhaseCheckView>> HandleAsync(EvaluateReportsPhaseCheckQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var started = time.GetTimestamp();
        await Flight.WaitAsync(cancellationToken);
        try
        {
            var readings = new List<Reading>
            {
                await ReadAsync("GetReportSummary", "summary", false, summary, new GetReportSummaryQuery(Query()), r => r.Evidence, cancellationToken),
                await ReadAsync("GetReportGroups", "groups", false, groups, new GetReportGroupsQuery(Query("basket")), r => r.Evidence, cancellationToken),
                await ReadAsync("GetReportPivot", "pivot", false, pivot, new GetReportPivotQuery(Query("strategy,timeframe")), r => r.Evidence, cancellationToken),
                await ReadAsync("GetReportEquity", "equity", false, equity, new GetReportEquityQuery(Query("strategy")), r => r.Evidence, cancellationToken),
                await ReadAsync("GetReportDistribution", "distribution", false, distribution, new GetReportDistributionQuery(Query("strategy")), r => r.Evidence, cancellationToken),
                await ReadAsync("GetReportMaeMfe", "mae-mfe", false, maeMfe, new GetReportMaeMfeQuery(Query("strategy")), r => r.Evidence, cancellationToken),
                await ReadAsync("GetReportInsights", "insights", false, insights, new GetReportInsightsQuery(Query()), r => r.Evidence, cancellationToken),
                await ReadAsync("GetReportWhatWorks", "what-works", true, whatWorks, new GetReportWhatWorksQuery(Query()), r => r.Evidence, cancellationToken),
                await ReadAsync("ListReportTrades", "trades", false, trades, new PageReportTradesQuery(Query(null, "1")), r => r.Evidence, cancellationToken),
            };
            var runList = await ReadAsync("ListReportRuns", "runs", true, runs, new ListReportRunsQuery(), null, cancellationToken);
            var facts = Facts(runList, readings);
            var report = ReportsPhaseCheck.Evaluate(facts);
            var text = report.ToText();
            var elapsed = (long)time.GetElapsedTime(started).TotalMilliseconds;
            var met = report.Criteria.Count(c => c.Verdict == PhaseVerdict.Met);
            var notMet = report.Criteria.Count(c => c.Verdict == PhaseVerdict.NotMet);
            var open = report.Criteria.Count(c => c.Verdict == PhaseVerdict.NotEvaluable);
            ReportsPhaseLog.Evaluated(logger, met, notMet, open, elapsed);
            return Result.Success(new ReportsPhaseCheckView(ToView(report), text));
        }
        finally
        {
            Flight.Release();
        }
    }

    private static ReportQuery Query(string? lens = null, string? limit = null) => ReportQuery.Parse(new ReportQueryInput(Lens: lens, Limit: limit)).Value;

    private static ReportsPhaseReportView ToView(ReportsPhaseReport report) => new(
        report.Headline(),
        [.. report.Criteria.Select(c => new LearningPhaseCriterionView(c.Number, c.Name, PhaseCheckReport.Label(c.Verdict), c.Detail, c.Caveat))],
        new ReportsPhaseAnswerView(report.Answer.Kind, report.Answer.Sentence, report.Answer.CellsLooked, report.Answer.LargestN, report.Answer.RequiredN),
        [.. report.Timings.Select(t => new ReportsPhaseTimingView(t.Operation, t.AnswerPath, t.ColdMs, t.WarmMs))],
        report.Runs,
        report.Trades,
        report.BasketsWithTrades,
        report.BuiltReports,
        report.Limits);

    private static string Sentence(WhatWorksView view)
    {
        if (view.Kind != "ranked" || view.Ranked.Count == 0)
        {
            return string.Create(CultureInfo.InvariantCulture, $"Nothing has enough evidence yet: the largest sample is {view.LargestN} of {view.RequiredTrades} needed ({view.CellsLookedAt} cells looked at). {view.Headline}");
        }

        var top = view.Ranked[0].Group;
        var bound = top.ExpectancyLowerR.Number is { } b ? string.Create(CultureInfo.InvariantCulture, $"lower bound {b:0.00} R") : "lower bound not available";
        return string.Create(CultureInfo.InvariantCulture, $"{view.Headline} Top cell {top.Key}: {bound}, n = {top.N}.");
    }

    private static string? EvidenceProblem(ReportEvidenceView? evidence)
    {
        if (evidence is null)
        {
            return "no evidence block";
        }

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(evidence.Basis))
        {
            missing.Add("basis");
        }

        if (evidence.N < 0)
        {
            missing.Add("n");
        }

        if (evidence.RunKeys.Count == 0)
        {
            missing.Add("run keys");
        }

        if (evidence.Limits.Count == 0)
        {
            missing.Add("limits");
        }

        return missing.Count == 0 ? null : "missing " + string.Join(", ", missing);
    }

    private ReportsPhaseFacts Facts(Reading runList, List<Reading> readings)
    {
        var runCount = (runList.First as RunsView)?.Runs.Count ?? 0;
        var summaryView = readings[0].First as SummaryView;
        var groupsView = readings[1].First as GroupsView;
        var works = readings.First(r => r.Report == "what-works");
        var answer = runCount == 0 ? ReportsPhaseAnswer.NoRuns
            : works.First is WhatWorksView w ? new ReportsPhaseAnswer(w.Kind, Sentence(w), w.CellsLookedAt, w.LargestN, w.RequiredTrades)
            : new ReportsPhaseAnswer("no_runs", "The What-works view could not be read: " + works.Code, 0, 0, 0);

        var facts = new List<ReportsPhaseReportFact>();
        var breaks = new List<string>();
        var sensitive = new SortedSet<string>(StringComparer.Ordinal);
        var metrics = 0;
        var differs = new List<string>();
        var compared = 0;
        var notCompared = new List<string>();
        foreach (var reading in readings)
        {
            if (reading.First is null)
            {
                ReportsPhaseLog.Unreadable(logger, reading.Report, reading.Code ?? "unknown");
                facts.Add(new ReportsPhaseReportFact(reading.Report, false, false, reading.Code));
                notCompared.Add(reading.Report);
                continue;
            }

            var problem = EvidenceProblem(reading.Evidence);
            facts.Add(new ReportsPhaseReportFact(reading.Report, true, problem is null, problem));
            var scan = ReportViewScan.Of(reading.First, reading.Report);
            metrics += scan.Metrics;
            breaks.AddRange(scan.Breaks);
            sensitive.UnionWith(scan.SensitiveNames);
            if (reading.Second is null || scan.Truncated)
            {
                notCompared.Add(reading.Report);
            }
            else
            {
                compared++;
                if (ReportViewScan.Of(reading.Second, reading.Report).Hash != scan.Hash)
                {
                    differs.Add(reading.Report);
                }
            }
        }

        var timings = readings.Append(runList).Select(r => new ReportsPhaseTiming(r.Operation, r.AnswerPath, r.ColdMs, r.WarmMs)).ToList();
        var defaultSet = DefaultSetValid(readings);
        var complete = notCompared.Count == 0;
        var baskets = groupsView?.Groups.Count(g => g.N > 0 && g.Parts.Any(p => p.Dimension == "basket" && p.Value is not null)) ?? 0;
        return new ReportsPhaseFacts(
            runCount,
            summaryView?.All.N ?? 0,
            baskets,
            answer,
            timings,
            facts,
            metrics,
            breaks,
            ReportsPhaseProbes.OverlapRefused(),
            ReportsPhaseProbes.MixedBasisRefused(),
            defaultSet,
            catalogue.Routes(),
            complete ? differs.Count == 0 : differs.Count > 0 ? false : null,
            complete ? (differs.Count == 0 ? $"{compared} reports compared" : "differs: " + string.Join(", ", differs)) : "not compared: " + string.Join(", ", notCompared),
            [.. sensitive],
            null,
            null,
            runList.First is not null);
    }

    /// <summary>The default set is valid when its loads were not refused for an overlap or a mixed basis; a refusal for another reason (too large) leaves it unread.</summary>
    private static bool? DefaultSetValid(List<Reading> readings)
    {
        if (readings.Any(r => r.Code is "reports.run_overlap" or "reports.mixed_basis"))
        {
            return false;
        }

        return readings.Any(r => r.First is not null) ? true : null;
    }

    private async ValueTask<Reading> ReadAsync<TQuery, TView>(
        string operation,
        string report,
        bool answerPath,
        IQueryHandler<TQuery, Result<TView>> handler,
        TQuery query,
        Func<TView, ReportEvidenceView>? evidence,
        CancellationToken cancellationToken)
        where TQuery : IQuery<Result<TView>>
        where TView : class
    {
        var cold = time.GetTimestamp();
        var first = await handler.HandleAsync(query, cancellationToken);
        var coldMs = (long)time.GetElapsedTime(cold).TotalMilliseconds;
        if (first.IsFailure)
        {
            return new Reading(operation, report, answerPath, null, null, null, null, null, first.Error.Code);
        }

        var warm = time.GetTimestamp();
        var second = await handler.HandleAsync(query, cancellationToken);
        var warmMs = (long)time.GetElapsedTime(warm).TotalMilliseconds;
        return new Reading(operation, report, answerPath, first.Value, second.IsSuccess ? second.Value : null, evidence?.Invoke(first.Value), coldMs, second.IsSuccess ? warmMs : null, second.IsFailure ? second.Error.Code : null);
    }

    private sealed record Reading(string Operation, string Report, bool AnswerPath, object? First, object? Second, ReportEvidenceView? Evidence, long? ColdMs, long? WarmMs, string? Code);
}
