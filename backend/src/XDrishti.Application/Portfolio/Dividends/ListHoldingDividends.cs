using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Dividends;

/// <summary>The dividends entered for a holding, newest first, voided ones included and flagged: <c>page</c> from 1, <c>pageSize</c> up to 200 (default 50).</summary>
public sealed record ListHoldingDividendsQuery(Guid HoldingId, int? Page, int? PageSize) : IQuery<Result<DividendPage>>;

internal sealed class ListHoldingDividendsHandler(IHoldingQueries holdings, IDividendStore dividends) : IQueryHandler<ListHoldingDividendsQuery, Result<DividendPage>>
{
    public const int MaxPageSize = 200;

    public const int DefaultPageSize = 50;

    public const int MaxPage = 100_000;

    public async ValueTask<Result<DividendPage>> HandleAsync(ListHoldingDividendsQuery query, CancellationToken cancellationToken)
    {
        var paging = Paging(query.Page, query.PageSize);
        if (paging.IsFailure)
        {
            return Result.Failure<DividendPage>(paging.Error);
        }

        var (page, pageSize) = paging.Value;
        if (await holdings.GetAsync(query.HoldingId, cancellationToken) is null)
        {
            return Result.Failure<DividendPage>(PortfolioErrors.HoldingNotFound);
        }

        var (items, total) = await dividends.ListForHoldingAsync(query.HoldingId, page, pageSize, cancellationToken);
        return Result.Success(new DividendPage([.. items.Select(DividendViews.Dividend)], page, pageSize, total));
    }

    /// <summary>Checks <c>page</c> and <c>pageSize</c> (the same limits for the lists of dividends and suggestions).</summary>
    internal static Result<(int Page, int PageSize)> Paging(int? requestedPage, int? requestedPageSize)
    {
        var errors = new FieldErrors();
        var page = requestedPage ?? 1;
        if (page is < 1 or > MaxPage)
        {
            errors.Add("page", $"Use 1 to {MaxPage}.");
        }

        var pageSize = requestedPageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaxPageSize)
        {
            errors.Add("pageSize", $"Use 1 to {MaxPageSize}.");
        }

        return errors.HasErrors ? Result.Failure<(int, int)>(PortfolioErrors.Invalid(errors)) : Result.Success((page, pageSize));
    }
}
