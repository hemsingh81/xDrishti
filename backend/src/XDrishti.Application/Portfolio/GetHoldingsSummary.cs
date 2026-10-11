using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Returns;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Portfolio;

/// <summary>
/// Totals of the holdings of one account, one bucket, or (without an account) every active account that has the Portfolio role and counts in totals:
/// invested, current value, unrealised and realised P&amp;L, XIRR from the union of all cash flows, counts, lots on the default date and the last sync.
/// </summary>
public sealed record GetHoldingsSummaryQuery(Guid? AccountId, Guid? BucketId) : IQuery<Result<HoldingsSummary>>;

internal sealed class GetHoldingsSummaryHandler(
    IHoldingQueries queries,
    IHoldingSyncStore syncs,
    IAccountStore accounts,
    TimeProvider clock) : IQueryHandler<GetHoldingsSummaryQuery, Result<HoldingsSummary>>
{
    public async ValueTask<Result<HoldingsSummary>> HandleAsync(GetHoldingsSummaryQuery query, CancellationToken cancellationToken)
    {
        if (query.AccountId is { } accountId && await accounts.FindReadOnlyAsync(accountId, cancellationToken) is null)
        {
            return Result.Failure<HoldingsSummary>(AccountErrors.NotFound);
        }

        var valuationDate = IndiaTime.Today(clock.GetUtcNow());
        var holdings = await queries.ListAsync(new HoldingFilter(query.AccountId, query.BucketId, null, null, TotalsOnly: query.AccountId is null), cancellationToken);
        var figures = ReturnsCalculator.ForGroup(holdings, valuationDate);
        var open = holdings.Where(h => h.IsOpen).ToList();
        var last = await syncs.GetLastAsync(query.AccountId, cancellationToken);
        return Result.Success(new HoldingsSummary(
            HoldingViews.Returns(figures),
            open.Count,
            holdings.Count - open.Count,
            open.Count(h => h.InstrumentId is null),
            open.Count(h => !h.IsPriced),
            open.Sum(h => h.Lots.Count(l => l.IsOpen && l.UsesDefaultDate)),
            valuationDate,
            last is null ? null : HoldingViews.Run(last)));
    }
}
