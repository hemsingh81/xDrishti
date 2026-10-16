using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>
/// The ledger entries of an account read from Dhan (ADR 0025), newest first: <c>from</c> and <c>to</c> (India dates, inclusive; open when missing), <c>class</c> (funds_in, funds_out, dividend, charges, other; all when missing), <c>page</c> from 1
/// and <c>pageSize</c> up to 200 (default 50). Alongside the page, the totals of every class for the period (whatever class is filtered). Read only; needs only that the account exists.
/// </summary>
public sealed record ListLedgerQuery(Guid AccountId, DateOnly? From, DateOnly? To, string? Class, int? Page, int? PageSize) : IQuery<Result<LedgerPage>>;

internal sealed class ListLedgerHandler(IAccountStore accounts, ILedgerStore ledger) : IQueryHandler<ListLedgerQuery, Result<LedgerPage>>
{
    public const int MaxPageSize = 200;

    public const int DefaultPageSize = 50;

    public const int MaxPage = 100_000;

    public async ValueTask<Result<LedgerPage>> HandleAsync(ListLedgerQuery query, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var page = query.Page ?? 1;
        if (page is < 1 or > MaxPage)
        {
            errors.Add("page", $"Use 1 to {MaxPage}.");
        }

        var pageSize = query.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaxPageSize)
        {
            errors.Add("pageSize", $"Use 1 to {MaxPageSize}.");
        }

        LedgerClass? entryClass = null;
        if (!string.IsNullOrWhiteSpace(query.Class))
        {
            if (LedgerClasses.TryParse(query.Class.Trim(), out var parsed))
            {
                entryClass = parsed;
            }
            else
            {
                errors.Add("class", "Use funds_in, funds_out, dividend, charges or other.");
            }
        }

        if (query.From is { } from && query.To is { } to && from > to)
        {
            errors.Add("from", "Cannot be after the end date.");
        }

        if (errors.HasErrors)
        {
            return Result.Failure<LedgerPage>(PortfolioErrors.Invalid(errors));
        }

        if (await accounts.FindReadOnlyAsync(query.AccountId, cancellationToken) is null)
        {
            return Result.Failure<LedgerPage>(AccountErrors.NotFound);
        }

        var (items, total) = await ledger.ListAsync(query.AccountId, query.From, query.To, entryClass, page, pageSize, cancellationToken);
        var totals = await ledger.TotalsAsync(query.AccountId, query.From, query.To, cancellationToken);
        return Result.Success(new LedgerPage([.. items.Select(BrokerHistoryViews.Entry)], page, pageSize, total, BrokerHistoryViews.Totals(totals), await ledger.FirstDateAsync(query.AccountId, cancellationToken)));
    }
}
