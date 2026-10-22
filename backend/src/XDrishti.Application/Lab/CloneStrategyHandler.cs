using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Lab;

internal sealed class CloneStrategyHandler(IStrategyStore store, IStrategyChecker checker) : IQueryHandler<CloneStrategyQuery, Result<ClonedStrategy>>
{
    public async ValueTask<Result<ClonedStrategy>> HandleAsync(CloneStrategyQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var source = await store.GetVersionAsync(query.Id, query.Version, cancellationToken);
        if (source is null)
        {
            return Result.Failure<ClonedStrategy>(LabErrors.NotFound(query.Id, query.Version));
        }

        var family = await store.GetAsync(query.Id, cancellationToken);
        var newId = query.NewId ?? (family is { BuiltIn: true } ? source.StrategyId + "_COPY" : source.StrategyId);
        if (!StrategyIdFormat.IsValid(newId))
        {
            return Result.Failure<ClonedStrategy>(Error.Validation(StrategyStoreErrors.InvalidCode, "The strategy id must be a letter followed by up to 63 letters, digits or underscores."));
        }

        var text = checker.Retarget(source.SourceText, newId);
        return text is null
            ? Result.Failure<ClonedStrategy>(Error.Conflict(LabErrors.StoredUnreadableCode, "The stored text no longer reads under the rules of today, so it cannot be cloned; download it instead."))
            : Result.Success(new ClonedStrategy(newId, text, source.Hash, source.StrategyId, source.Version));
    }
}
