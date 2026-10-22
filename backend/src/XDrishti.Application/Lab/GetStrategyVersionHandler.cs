using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Lab;

internal sealed class GetStrategyVersionHandler(IStrategyStore store, IStrategyChecker checker) : IQueryHandler<GetStrategyVersionQuery, Result<StrategyVersionView>>
{
    public async ValueTask<Result<StrategyVersionView>> HandleAsync(GetStrategyVersionQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var v = await store.GetVersionAsync(query.Id, query.Version, cancellationToken);
        return v is null
            ? Result.Failure<StrategyVersionView>(LabErrors.NotFound(query.Id, query.Version))
            : Result.Success(new StrategyVersionView(v.StrategyId, v.Version, v.Hash, v.ParentHash, v.Note, v.CreatedAt, v.SourceText, checker.Check(v.SourceText).Description is not null));
    }
}
