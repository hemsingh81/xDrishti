namespace XDrishti.Domain.Readiness;

/// <summary>
/// The two gates of the cell lifecycle (spec CF-11 D1 and D6). <b>Gate R</b> (a cell goes from Candidate to Paper): rows 1 to 12 and 14 all pass. <b>Gate S</b> (a cell goes from Paper to Active, and later scale-up): Gate R, rows 13 and 15, and the label "Ready for the next
/// phase". A row that is unavailable is not a pass, so it blocks. Pure; deciding what a failed gate does (a warning, or a refusal when the owner's enforce switch is ON) is not here.
/// </summary>
public static class ReadinessGates
{
    /// <summary>Gate R.</summary>
    public const string R = "R";

    /// <summary>Gate S.</summary>
    public const string S = "S";

    private static readonly int[] RowsR = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 14];

    private static readonly int[] RowsS = [.. RowsR, 13, 15];

    /// <summary>Gate R over the checklist rows.</summary>
    public static GateResult GateR(IReadOnlyList<ChecklistRow> rows)
    {
        var failing = Failing(rows, RowsR);
        return new GateResult(R, failing.Count == 0, failing, true);
    }

    /// <summary>Gate S over the checklist rows and the label of the strategy's score.</summary>
    public static GateResult GateS(IReadOnlyList<ChecklistRow> rows, ReadinessLabel label)
    {
        var failing = Failing(rows, RowsS);
        var labelOk = label == ReadinessLabel.ReadyForNextPhase;
        return new GateResult(S, failing.Count == 0 && labelOk, failing, labelOk);
    }

    private static List<ChecklistRow> Failing(IReadOnlyList<ChecklistRow> rows, int[] required)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var failing = new List<ChecklistRow>();
        foreach (var number in required)
        {
            var row = rows.FirstOrDefault(r => r.Number == number);
            if (row is null || row.Status != RowStatus.Pass)
            {
                failing.Add(row ?? new ChecklistRow(number, "missing row", "none", RowStatus.Unavailable, string.Empty, "the row was not evaluated"));
            }
        }

        return failing;
    }
}
