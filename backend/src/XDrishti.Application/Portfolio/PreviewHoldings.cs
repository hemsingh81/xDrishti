using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>
/// Fetches the holdings of a connected Dhan account without saving anything, for the first import in the portfolio settings. Needs the Portfolio role and a usable Dhan token
/// (<c>portfolio.not_connected</c> otherwise).
/// </summary>
public sealed record PreviewHoldingsQuery(Guid AccountId) : IQuery<Result<ImportPreview>>;

internal sealed class PreviewHoldingsHandler(HoldingsFetcher fetcher, IHoldingStore holdings)
    : IQueryHandler<PreviewHoldingsQuery, Result<ImportPreview>>
{
    public async ValueTask<Result<ImportPreview>> HandleAsync(PreviewHoldingsQuery query, CancellationToken cancellationToken)
    {
        var account = await fetcher.AccountAsync(query.AccountId, cancellationToken);
        if (account.IsFailure)
        {
            return Result.Failure<ImportPreview>(account.Error);
        }

        var connected = await fetcher.ConnectedAsync(query.AccountId, cancellationToken);
        if (connected.IsFailure)
        {
            return Result.Failure<ImportPreview>(connected.Error);
        }

        var existing = await holdings.ListForAccountAsync(query.AccountId, cancellationToken);
        var fetched = await fetcher.FetchAsync(query.AccountId, cancellationToken);
        if (fetched.IsFailure)
        {
            return Result.Failure<ImportPreview>(fetched.Error);
        }

        var defaultDate = account.Value.Portfolio.ImportDate;
        var index = new HoldingIndex(existing);
        var rows = fetched.Value.Rows.Select(row =>
        {
            var instrument = fetched.Value.Instruments.GetValueOrDefault(row.Key);
            var match = index.Find(row);
            var imported = match is { Status: HoldingStatus.Open };
            var suggested = imported ? match!.OpenLotsOldestFirst().FirstOrDefault()?.AcquiredOn ?? defaultDate : defaultDate;
            return new PreviewRow(
                row.Key,
                row.TradingSymbol,
                instrument?.Name ?? row.TradingSymbol,
                row.Exchange,
                row.Qty,
                row.AvgCost,
                row.LastPrice,
                instrument?.Id,
                instrument?.CanonicalSymbol,
                instrument is not null,
                imported,
                suggested);
        }).ToList();
        return Result.Success(new ImportPreview(defaultDate, rows));
    }
}
