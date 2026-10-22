using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>Reads one cell (spec P6-01).</summary>
internal sealed class GetCellHandler(ICellStatsStore store) : IQueryHandler<GetCellQuery, Result<IReadOnlyList<CellStatsView>>>
{
    public async ValueTask<Result<IReadOnlyList<CellStatsView>>> HandleAsync(GetCellQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.CellKey) || query.CellKey.Length > 400)
        {
            return Result.Failure<IReadOnlyList<CellStatsView>>(LearningErrors.CellNotFound(query.CellKey ?? string.Empty));
        }

        var rows = await store.FindLatestAsync(query.CellKey, cancellationToken);
        return rows.Count == 0
            ? Result.Failure<IReadOnlyList<CellStatsView>>(LearningErrors.CellNotFound(query.CellKey))
            : Result.Success<IReadOnlyList<CellStatsView>>([.. rows.Select(CellStatsView.Of)]);
    }
}
