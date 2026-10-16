using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Portfolio.Dividends;

/// <summary>The dividends entered for an account — every holding's and the unallocated ones — newest first, voided ones included and flagged: <c>page</c> from 1, <c>pageSize</c> up to 200 (default 50).</summary>
public sealed record ListAccountDividendsQuery(Guid AccountId, int? Page, int? PageSize) : IQuery<Result<DividendPage>>;

internal sealed class ListAccountDividendsHandler(IAccountStore accounts, IDividendStore dividends) : IQueryHandler<ListAccountDividendsQuery, Result<DividendPage>>
{
    public async ValueTask<Result<DividendPage>> HandleAsync(ListAccountDividendsQuery query, CancellationToken cancellationToken)
    {
        var paging = ListHoldingDividendsHandler.Paging(query.Page, query.PageSize);
        if (paging.IsFailure)
        {
            return Result.Failure<DividendPage>(paging.Error);
        }

        var (page, pageSize) = paging.Value;
        if (await accounts.FindReadOnlyAsync(query.AccountId, cancellationToken) is null)
        {
            return Result.Failure<DividendPage>(AccountErrors.NotFound);
        }

        var (items, total) = await dividends.ListForAccountAsync(query.AccountId, page, pageSize, cancellationToken);
        return Result.Success(new DividendPage([.. items.Select(DividendViews.Dividend)], page, pageSize, total));
    }
}
