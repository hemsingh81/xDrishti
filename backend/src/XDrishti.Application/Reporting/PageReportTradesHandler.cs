using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>Pages the trades of the population (P8-06): closed trades only, each once, so the list is the very set the figures were computed from. A page is at most 1,000 trades.</summary>
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

        var population = loaded.Value.Population;
        var offset = query.Query.Offset;
        var size = query.Query.Page.Size;
        var lines = population.Trades(offset, size);
        var next = offset + lines.Count < population.TradeCount ? ReportQuery.CursorFor(offset + lines.Count) : null;
        return Result.Success(new TradesView([.. lines.Select(ReportViews.TradeLine)], population.TradeCount, next, ReportViews.Evidence(population.Evidence, loaded.Value)));
    }
}
