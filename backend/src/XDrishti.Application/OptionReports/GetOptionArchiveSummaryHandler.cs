using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.Abstractions.OptionCosts;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Application.Abstractions.OptionReports;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;
using XDrishti.Domain.Options.Quality;
using XDrishti.Domain.Options.Reports;

namespace XDrishti.Application.OptionReports;

/// <summary>
/// <c>GET /api/options/summary</c> (spec P14-16): per underlying the recorded and usable chain sessions, the first and last recorded date, the gaps by outcome, the backfill keys and the half-spread model state. Reads the stored quality results, the backfill's own key state and the
/// stored spread model through read ports; returns counts and dates only. A figure with no source is <c>unavailable</c> with its reason. The underlyings are those with a chain result in the last 400 days or a backfill key, at most 200.
/// </summary>
internal sealed class GetOptionArchiveSummaryHandler(IOptionReportReader reader, IBackfillFetchLog backfillLog, IOptionQualityStore quality, IOptionReference reference, ISpreadModelStore spreads, TimeProvider clock) : IQueryHandler<GetOptionArchiveSummaryQuery, Result<OptionArchiveSummaryView>>
{
    public const int MaxUnderlyings = 200;

    public const int WindowDays = 400;

    // shortcut: the backfill state is the same unbounded group-by the backfill itself loads each cycle, and the per-underlying quality and spread reads are one call each (at most 200 underlyings); none of them has a lower date bound. See ADR 0201.
    public async ValueTask<Result<OptionArchiveSummaryView>> HandleAsync(GetOptionArchiveSummaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var today = IndiaTime.Today(clock.GetUtcNow());
        var recorded = await reader.ArchiveUnderlyingsAsync(today.AddDays(1 - WindowDays), today, MaxUnderlyings, cancellationToken);
        var backfill = BackfillKeyCounts.From(await backfillLog.LoadStateAsync(cancellationToken));
        var ids = recorded.Concat(backfill.Keys).Distinct().Take(MaxUnderlyings).ToList();
        if (ids.Count == 0)
        {
            return Result.Success(new OptionArchiveSummaryView([]));
        }

        var symbols = await reference.SymbolsAsync(ids, cancellationToken);
        var items = new List<UnderlyingArchiveSummary>(ids.Count);
        foreach (var id in ids.OrderBy(i => symbols.GetValueOrDefault(i) ?? i.ToString("D"), StringComparer.Ordinal))
        {
            var rows = await quality.ChainResultsAsync(id, today, cancellationToken);
            var model = await spreads.LatestAsync(id, cancellationToken);
            items.Add(Summary(symbols.GetValueOrDefault(id) ?? id.ToString("D"), rows, backfill.GetValueOrDefault(id), model));
        }

        return Result.Success(new OptionArchiveSummaryView(items));
    }

    private static UnderlyingArchiveSummary Summary(string symbol, IReadOnlyList<OptionQualityRow> rows, BackfillKeyCounts? backfill, XDrishti.Domain.Options.Costs.HalfSpreadModel? model)
    {
        var none = ReportStates.Reasons.NotRecorded;
        var bySession = rows.GroupBy(r => r.SessionDate).ToDictionary(g => g.Key, g => (IReadOnlyList<QualityCheck>)[.. g.Select(r => r.Check)]);
        var figures = ArchiveSessionFigures.Of(bySession);
        var noModel = ReportStates.Reasons.NoModel;
        return new UnderlyingArchiveSummary(
            symbol,
            bySession.Count == 0 ? ReportedCount.None(none) : ReportedCount.Of(figures.Recorded),
            bySession.Count == 0 ? ReportedCount.None(none) : ReportedCount.Of(figures.Usable),
            figures.First is { } first ? ReportedDate.Of(first) : ReportedDate.None(none),
            figures.Last is { } last ? ReportedDate.Of(last) : ReportedDate.None(none),
            bySession.Count == 0 ? new GapsView(ReportStates.Unavailable, none, []) : new GapsView(ReportStates.Ok, null, [.. figures.Outcomes.Select(o => new SessionOutcomeView(o.Outcome, o.Reason, o.Sessions))]),
            backfill is null ? new BackfillView(ReportStates.Unavailable, none, null, null, null, null) : new BackfillView(ReportStates.Ok, null, backfill.Done, backfill.Empty, backfill.NoData, backfill.GivenUp),
            model is null ? ReportedCount.None(noModel) : ReportedCount.Of(model.Sessions),
            model is null ? ReportedDate.None(noModel) : ReportedDate.Of(model.FirstSession),
            model is null ? ReportedDate.None(noModel) : ReportedDate.Of(model.LastSession));
    }
}
