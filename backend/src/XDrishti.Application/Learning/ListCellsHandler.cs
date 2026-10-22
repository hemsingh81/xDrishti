using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Lists the rows of the newest cell-statistics snapshot (spec P6-01). An empty list is an answer: nothing was computed yet.</summary>
internal sealed class ListCellsHandler(ICellStatsStore store) : IQueryHandler<ListCellsQuery, Result<IReadOnlyList<CellStatsView>>>
{
    public async ValueTask<Result<IReadOnlyList<CellStatsView>>> HandleAsync(ListCellsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Window is not null && CellWindowExtensions.Parse(query.Window) is null)
        {
            return Result.Failure<IReadOnlyList<CellStatsView>>(LearningErrors.Invalid([new FieldError("window", "The window is all, 250d or 60d.")]));
        }

        var rows = await store.ListLatestAsync(string.IsNullOrWhiteSpace(query.Strategy) ? null : query.Strategy, query.Window, cancellationToken);
        return Result.Success<IReadOnlyList<CellStatsView>>([.. rows.Select(CellStatsView.Of)]);
    }
}
