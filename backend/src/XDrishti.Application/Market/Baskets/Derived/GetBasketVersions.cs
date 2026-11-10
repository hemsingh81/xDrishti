using XDrishti.Application.Abstractions.Market.Baskets;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Baskets;

namespace XDrishti.Application.Market.Baskets;

/// <summary>The published versions of a derived basket, newest first (<c>take</c> 1 to 200, default 50); 404 for a basket without versions' definition.</summary>
public sealed record GetBasketVersionsQuery(Guid BasketId, int Skip = 0, int Take = 50) : IQuery<Result<BasketVersionPage>>;

internal sealed class GetBasketVersionsHandler(IDerivedVersionStore store) : IQueryHandler<GetBasketVersionsQuery, Result<BasketVersionPage>>
{
    public async ValueTask<Result<BasketVersionPage>> HandleAsync(GetBasketVersionsQuery query, CancellationToken cancellationToken)
    {
        if (query.Skip < 0 || query.Take is < 1 or > 200)
        {
            return Result.Failure<BasketVersionPage>(BasketErrors.Invalid("take", "Use skip 0 or more and take 1 to 200."));
        }

        var page = await store.ListVersionsAsync(query.BasketId, query.Skip, query.Take, cancellationToken);
        return page is null ? Result.Failure<BasketVersionPage>(BasketErrors.NotFound) : Result.Success(page);
    }
}

/// <summary>One version with its scores, named exclusions and parameters.</summary>
public sealed record GetBasketScoresQuery(Guid BasketId, Guid VersionId) : IQuery<Result<BasketVersionDetail>>;

internal sealed class GetBasketScoresHandler(IDerivedVersionStore store) : IQueryHandler<GetBasketScoresQuery, Result<BasketVersionDetail>>
{
    public async ValueTask<Result<BasketVersionDetail>> HandleAsync(GetBasketScoresQuery query, CancellationToken cancellationToken)
    {
        var detail = await store.GetVersionAsync(query.BasketId, query.VersionId, cancellationToken);
        return detail is null ? Result.Failure<BasketVersionDetail>(BasketErrors.NotFound) : Result.Success(detail);
    }
}
