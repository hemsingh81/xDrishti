using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Lab;

internal sealed class GetStrategyHandler(IStrategyStore store) : IQueryHandler<GetStrategyQuery, Result<StrategyDetail>>
{
    public async ValueTask<Result<StrategyDetail>> HandleAsync(GetStrategyQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var family = await store.GetAsync(query.Id, cancellationToken);
        if (family is null)
        {
            return Result.Failure<StrategyDetail>(LabErrors.NotFound(query.Id));
        }

        var versions = await store.ListVersionsAsync(query.Id, cancellationToken);
        return Result.Success(new StrategyDetail(
            family.Id,
            family.Style,
            family.BuiltIn,
            family.CreatedAt,
            family.LatestVersion,
            [.. versions.Select(v => new VersionSummary(v.Version, v.Hash, v.ParentHash, v.Note, v.CreatedAt))]));
    }
}
