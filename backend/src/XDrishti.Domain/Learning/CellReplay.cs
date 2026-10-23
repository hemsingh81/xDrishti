namespace XDrishti.Domain.Learning;

/// <summary>The state a cell's changelog rows lead to, and what in them does not add up (spec P6-08).</summary>
/// <param name="State">The state after the last row, or <see cref="CellState.Research"/> without rows.</param>
/// <param name="Problems">Rows whose "from" is not the state before them, or that revert something other than the row just before them, or do not return to its "from"; empty when consistent.</param>
public sealed record CellReplay(CellState State, IReadOnlyList<string> Problems)
{
    /// <summary>Folds the rows of one cell, oldest first. The log is the state: nothing else is stored.</summary>
    public static CellReplay Of(IEnumerable<CellMove> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var state = CellState.Research;
        var problems = new List<string>();
        CellMove? previous = null;
        foreach (var row in rows)
        {
            if (row.From != state)
            {
                problems.Add($"Row {row.Id} starts from {row.From.Code()} but the cell was {state.Code()}.");
            }

            if (row.RevertsId is { } target)
            {
                if (previous is null || previous.Id != target)
                {
                    problems.Add($"Row {row.Id} reverts row {target}, which is not the row just before it.");
                }
                else if (row.To != previous.From)
                {
                    problems.Add($"Row {row.Id} reverts row {target} but leaves the cell in {row.To.Code()}, not the {previous.From.Code()} it was in before.");
                }
            }

            previous = row;
            state = row.To;
        }

        return new CellReplay(state, problems);
    }
}
