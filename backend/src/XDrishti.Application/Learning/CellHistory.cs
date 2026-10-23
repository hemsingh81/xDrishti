using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>Replays the stored rows of one cell (P6-08). A row whose state is not a state code is a break in the log and is reported, not skipped.</summary>
internal static class CellHistory
{
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
