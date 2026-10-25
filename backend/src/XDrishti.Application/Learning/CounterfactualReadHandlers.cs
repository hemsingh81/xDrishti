using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Planning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Planning;

namespace XDrishti.Application.Learning;

/// <summary>One stored counterfactual (spec P8-12). Read only: it fits nothing.</summary>
internal sealed class GetCounterfactualHandler(ILearningReportStore store) : IQueryHandler<GetCounterfactualQuery, Result<CounterfactualReportView>>
{
    public async ValueTask<Result<CounterfactualReportView>> HandleAsync(GetCounterfactualQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var stored = query.Id is { } id ? await store.FindAsync(id, cancellationToken) : await store.NewestAsync(CounterfactualStore.Kind, cancellationToken);
        if (stored is null || stored.Report.Kind != CounterfactualStore.Kind)
        {
            return Result.Failure<CounterfactualReportView>(Error.NotFound(LearningErrors.CounterfactualNotFoundCode, query.Id is { } n ? $"No stored counterfactual #{n}." : "No counterfactual is stored yet."));
        }

        // shortcut: reads the newest 50 rows to count the same runs; past ~50 stored reports use a COUNT(*) port method.
        var all = await store.ListAsync(CounterfactualStore.Kind, ListCounterfactualsHandler.Max, cancellationToken);
        var view = CounterfactualStore.ViewOf(stored, CounterfactualStore.SameRuns(stored, all));
        return view is null ? Result.Failure<CounterfactualReportView>(Error.NotFound(LearningErrors.CounterfactualNotFoundCode, "The stored counterfactual cannot be read.")) : Result.Success(view);
    }
}

/// <summary>The stored counterfactuals, newest first (spec P8-12). Read only.</summary>
internal sealed class ListCounterfactualsHandler(ILearningReportStore store) : IQueryHandler<ListCounterfactualsQuery, Result<IReadOnlyList<CounterfactualHeaderView>>>
{
    /// <summary>The most rows listed.</summary>
    public const int Max = 50;

    public async ValueTask<Result<IReadOnlyList<CounterfactualHeaderView>>> HandleAsync(ListCounterfactualsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > Max)
        {
            return Result.Failure<IReadOnlyList<CounterfactualHeaderView>>(LearningErrors.Invalid([new FieldError("limit", "The limit is 1 to 50.")]));
        }

        var rows = await store.ListAsync(CounterfactualStore.Kind, Max, cancellationToken);
        return Result.Success<IReadOnlyList<CounterfactualHeaderView>>([.. rows.Take(query.Limit).Select(r => CounterfactualStore.HeaderOf(r, CounterfactualStore.SameRuns(r, rows))).OfType<CounterfactualHeaderView>()]);
    }
}

/// <summary>The Journey (spec P8-12): four reads, aggregated in memory. The live line is not evaluable whatever else is stored.</summary>
internal sealed class GetJourneyHandler(ILearningReportStore store, IGradeStore grades) : IQueryHandler<GetJourneyQuery, Result<JourneyView>>
{
    /// <summary>The most graded tickets read.</summary>
    public const int GradeLimit = 5000;

    public async ValueTask<Result<JourneyView>> HandleAsync(GetJourneyQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var rows = await store.ListAsync(CounterfactualStore.Kind, ListCounterfactualsHandler.Max, cancellationToken);
        var newest = rows.Count == 0 ? null : rows[0];
        var header = newest is null ? null : CounterfactualStore.HeaderOf(newest, CounterfactualStore.SameRuns(newest, rows));
        var report = newest is null ? null : CounterfactualStore.ViewOf(newest, 0);
        var page = await grades.ReportItemsAsync(null, null, GradeLimit, cancellationToken);
        var weeks = JourneyWeeks.Build(page.Items).Select(JourneyWeekView.Of).ToList();
        var planPrice = new JourneyPlanPriceView(
            weeks.Count == 0 ? "empty" : "ok", weeks, weeks.Sum(w => w.Graded), TicketGrade.Basis,
            page.Truncated ? "More graded tickets matched than are read; the newest are shown and the oldest week may be partial." : null);
        var live = new JourneyLiveView("not_evaluable", "no graded real trade exists (paper fills arrive in P10, live in P11)", 0);
        var evidence = report is null
            ? new JourneyEvidenceView(null, [], null, null, 0, [])
            : new JourneyEvidenceView(report.Basis, [.. report.Sources.Select(s => s.RunKey).Distinct(StringComparer.Ordinal)], report.Cut, report.To, report.All?.Rows ?? 0, report.Limits);
        return Result.Success(new JourneyView(header, planPrice, live, evidence));
    }
}
