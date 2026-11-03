using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>The state of a cell by replaying its changelog rows (spec P6-08): the log is the state.</summary>
internal sealed class GetCellStateHandler(ILearningChangelog changelog) : IQueryHandler<GetCellStateQuery, Result<CellStateView>>
{
    public async ValueTask<Result<CellStateView>> HandleAsync(GetCellStateQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (CellId.TryParse(query.Cell) is not { } id)
        {
            return Result.Failure<CellStateView>(LearningErrors.Invalid([new FieldError("cell", "The cell is strategy|timeframe|side|style|exit|regime, with an optional universe and expression after it.")]));
        }

        var history = await changelog.HistoryAsync(query.Cell, cancellationToken);
        var replay = CellHistory.Replay(history);
        return Result.Success(new CellStateView(
            query.Cell,
            id.Universe,
            id.Expression.Code(),
            replay.State.Code(),
            [.. CellLifecycle.Next(replay.State).Select(s => s.Code())],
            history.Count,
            replay.Problems,
            CellLifecycle.DefaultPaperDaysBeforeActive,
            "A record only: nothing acts on a cell's state. Paper needs paper data (P10) and Active needs live (P7 and later); an Active state needs the paper period and the owner."));
    }
}
