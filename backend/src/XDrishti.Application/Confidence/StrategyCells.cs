using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Confidence;

/// <summary>Reads where the cells of a strategy are, replayed from the changelog (P6-08). A cell with no row is in research and is not a cell a kill rule can pause. Read only.</summary>
internal sealed class StrategyCells(ILearningChangelog changelog)
{
    /// <summary>The cells of the strategy that are now <c>Paper</c> or <c>Active</c> (the ones a kill rule pauses), in key order. A cell whose rows do not add up is left out.</summary>
    public async ValueTask<IReadOnlyList<string>> DemotableAsync(string strategy, CancellationToken cancellationToken)
    {
        var result = new List<string>();
        foreach (var cell in await changelog.CellSubjectsAsync(strategy + "|", cancellationToken))
        {
            if (await StateAsync(cell, cancellationToken) is CellState.Paper or CellState.Active)
            {
                result.Add(cell);
            }
        }

        return result;
    }

    /// <summary>The state of one cell, or null when its rows do not add up.</summary>
    public async ValueTask<CellState?> StateAsync(string cell, CancellationToken cancellationToken)
    {
        var replay = CellHistory.Replay(await changelog.HistoryAsync(cell, cancellationToken));
        return replay.Problems.Count > 0 ? null : replay.State;
    }
}
