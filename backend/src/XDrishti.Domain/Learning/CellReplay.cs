namespace XDrishti.Domain.Learning;

/// <summary>The state a cell's changelog rows lead to, and what in them does not add up (spec P6-08).</summary>
/// <param name="State">The state after the last row, or <see cref="CellState.Research"/> without rows.</param>
/// <param name="Problems">Rows whose "from" is not the state before them, or that revert something that is not an earlier row; empty when consistent.</param>
public sealed record CellReplay(CellState State, IReadOnlyList<string> Problems)
{
    /// <summary>Folds the rows of one cell, oldest first. The log is the state: nothing else is stored.</summary>
    public static CellReplay Of(IEnumerable<CellMove> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var state = CellState.Research;
        var problems = new List<string>();
        var seen = new HashSet<long>();
        foreach (var row in rows)
        {
            if (row.From != state)
            {
                problems.Add($"Row {row.Id} starts from {row.From.Code()} but the cell was {state.Code()}.");
            }

            if (row.RevertsId is { } target && !seen.Contains(target))
            {
                problems.Add($"Row {row.Id} reverts row {target}, which is not an earlier row of this cell.");
            }

            seen.Add(row.Id);
            state = row.To;
        }

        return new CellReplay(state, problems);
    }
}
