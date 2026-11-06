using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Replays the stored rows of one cell (P6-08). A row whose state is not a state code is a break in the log and is reported, not skipped.</summary>
internal static class CellHistory
{
    /// <summary>
    /// The row that put a cell into the pause it is in now, found by replaying the rows in order: a transition to Paused is the origin; a transition away from Paused clears it; a revert restores the origin the cell had before the row it reverted (so a pause that is
    /// restored by a revert still points at the pause that was first made). Null when the cell is not paused or the rows do not say. Used to tell a kill pause from any other (CF-12 part 12b).
    /// </summary>
    public static ChangelogEntry? PauseOrigin(IReadOnlyList<ChangelogEntry> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var before = new Dictionary<long, ChangelogEntry?>();
        ChangelogEntry? origin = null;
        foreach (var row in rows)
        {
            before[row.Id] = origin;
            var to = CellStateExtensions.Parse(row.Entry.ToState);
            if (row.Entry.RevertsId is { } reverted && before.TryGetValue(reverted, out var earlier))
            {
                origin = earlier; // the state before the reverted row is back
            }
            else if (to == CellState.Paused)
            {
                origin = row;
            }
            else
            {
                origin = null;
            }
        }

        return origin;
    }

    public static CellReplay Replay(IReadOnlyList<ChangelogEntry> rows)
    {
        var moves = new List<CellMove>(rows.Count);
        var broken = new List<string>();
        foreach (var row in rows)
        {
            var from = CellStateExtensions.Parse(row.Entry.FromState);
            var to = CellStateExtensions.Parse(row.Entry.ToState);
            if (from is null || to is null)
            {
                broken.Add($"Row {row.Id} has a state that is not a cell state.");
                continue;
            }

            moves.Add(new CellMove(row.Id, from.Value, to.Value, row.Entry.RevertsId));
        }

        var replay = CellReplay.Of(moves);
        return broken.Count == 0 ? replay : replay with { Problems = [.. broken, .. replay.Problems] };
    }
}
