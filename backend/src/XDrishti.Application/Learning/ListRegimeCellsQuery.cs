using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>The regime cells of the newest snapshot that holds any (P6-02, ADR 0146), optionally of one strategy and one window (<c>all</c>, <c>250d</c>, <c>60d</c>); empty before a computation with the regime split. <c>learning.invalid</c> for another window code.</summary>
public sealed record ListRegimeCellsQuery(string? Strategy, string? Window) : IQuery<Result<IReadOnlyList<CellStatsView>>>;

internal sealed class ListRegimeCellsHandler(ICellStatsStore store) : IQueryHandler<ListRegimeCellsQuery, Result<IReadOnlyList<CellStatsView>>>
{
    public async ValueTask<Result<IReadOnlyList<CellStatsView>>> HandleAsync(ListRegimeCellsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Window is not null && CellWindowExtensions.Parse(query.Window) is null)
        {
            return Result.Failure<IReadOnlyList<CellStatsView>>(LearningErrors.Invalid([new FieldError("window", "The window is all, 250d or 60d.")]));
        }

        var rows = await store.ListRegimeLatestAsync(string.IsNullOrWhiteSpace(query.Strategy) ? null : query.Strategy, query.Window, cancellationToken);
        return Result.Success<IReadOnlyList<CellStatsView>>([.. rows.Select(CellStatsView.Of)]);
    }
}
