using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Reporting;

namespace XDrishti.Application.Reporting;

/// <summary>
/// Pages the trades of the population (P8-06): closed trades only, each once, so the list is the very set the figures were computed from. A page is at most 1,000 trades. Every page recomputes the population (there is no stored state between pages); the cursor carries an offset
/// and a short hash of the runs and the count it was made for, and a cursor from another report is refused (422) instead of paging through a list that changed under it.
/// </summary>
internal sealed class PageReportTradesHandler(ReportLoader loader) : IQueryHandler<PageReportTradesQuery, Result<TradesView>>
{
    public async ValueTask<Result<TradesView>> HandleAsync(PageReportTradesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var loaded = await loader.LoadAsync(query.Query, "trades", cancellationToken);
        if (loaded.IsFailure)
        {
            return Result.Failure<TradesView>(loaded.Error);
        }

        using var report = loaded.Value;
        var population = report.Population;
        var hash = ReportQuery.ReportHash(report.Runs.Select(r => r.RunKey), population.TradeCount);
        if (query.Query.CursorHash is { } given && !string.Equals(given, hash, StringComparison.Ordinal))
        {
            return Result.Failure<TradesView>(ReportErrors.Invalid("The cursor belongs to another report (other runs, filters or trades); start again without a cursor."));
        }

        var offset = query.Query.Offset;
        var lines = population.Trades(offset, query.Query.Page.Size);
        var next = offset + lines.Count < population.TradeCount ? ReportQuery.CursorFor(offset + lines.Count, hash) : null;
        return Result.Success(new TradesView([.. lines.Select(ReportViews.TradeLine)], population.TradeCount, next, ReportViews.Evidence(population.Evidence, report)));
    }
}
