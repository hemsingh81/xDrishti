using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.BrokerHistory;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Portfolio.Dividends;

/// <summary>The suggested dividends of an account (ADR 0026): its ledger entries of class dividend that the owner has not accepted or dismissed and that could be entered as a dividend (an amount above the highest one is not offered), newest first: <c>page</c> from 1, <c>pageSize</c> up to 200 (default 50). Read only.</summary>
public sealed record ListDividendSuggestionsQuery(Guid AccountId, int? Page, int? PageSize) : IQuery<Result<DividendSuggestionPage>>;

internal sealed class ListDividendSuggestionsHandler(IAccountStore accounts, IDividendSuggestionStore suggestions) : IQueryHandler<ListDividendSuggestionsQuery, Result<DividendSuggestionPage>>
{
    public async ValueTask<Result<DividendSuggestionPage>> HandleAsync(ListDividendSuggestionsQuery query, CancellationToken cancellationToken)
    {
        var paging = ListHoldingDividendsHandler.Paging(query.Page, query.PageSize);
        if (paging.IsFailure)
        {
            return Result.Failure<DividendSuggestionPage>(paging.Error);
        }

        var (page, pageSize) = paging.Value;
        if (await accounts.FindReadOnlyAsync(query.AccountId, cancellationToken) is null)
        {
            return Result.Failure<DividendSuggestionPage>(AccountErrors.NotFound);
        }

        var (items, total) = await suggestions.ListOpenAsync(query.AccountId, page, pageSize, cancellationToken);
        return Result.Success(new DividendSuggestionPage([.. items.Select(BrokerHistoryViews.Entry)], page, pageSize, total));
    }
}
