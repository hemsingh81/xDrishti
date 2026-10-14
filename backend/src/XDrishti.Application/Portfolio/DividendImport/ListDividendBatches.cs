using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>The dividend import batches, newest first, one page: <c>accountId</c> to see one account's (default all), <c>page</c> from 1, <c>pageSize</c> up to 100 (default 50).</summary>
public sealed record ListDividendBatchesQuery(Guid? AccountId, int? Page, int? PageSize) : IQuery<Result<DividendBatchPage>>;

internal sealed class ListDividendBatchesHandler(IDividendBatches batches, IAccountStore accounts) : IQueryHandler<ListDividendBatchesQuery, Result<DividendBatchPage>>
{
    public const int MaxPageSize = 100;

    public const int DefaultPageSize = 50;

    public const int MaxPage = 100_000;

    public async ValueTask<Result<DividendBatchPage>> HandleAsync(ListDividendBatchesQuery query, CancellationToken cancellationToken)
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

        if (errors.HasErrors)
        {
            return Result.Failure<DividendBatchPage>(PortfolioErrors.Invalid(errors));
        }

        var (items, total) = await batches.ListAsync(query.AccountId, page, pageSize, cancellationToken);
        var names = await accounts.ListPortfolioAccountNamesAsync(cancellationToken);
        return Result.Success(new DividendBatchPage([.. items.Select(i => DividendImportViews.Batch(i, names.GetValueOrDefault(i.Batch.AccountId, "Account")))], page, pageSize, total));
    }
}
