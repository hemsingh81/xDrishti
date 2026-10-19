namespace XDrishti.SimOracle;

/// <summary>The 16 categories and the trace rows that count as reaching them.</summary>
public static class OracleCategories
{
    private static readonly string[][] Markers =
    [
        [OracleRows.E5Gap],
        [OracleRows.E6Gap],
        [OracleRows.XStopGap],
        [OracleRows.XTargetGap],
        [OracleRows.X4Between],
        [OracleRows.X4Beyond],
        [OracleRows.E1Hole],
        [OracleRows.XHole],
        [OracleRows.X1Late, OracleRows.X8Last],
        [OracleRows.TieLimit, OracleRows.TieStop, OracleRows.TieTarget, OracleRows.GridOff],
        [OracleRows.E0],
        [OracleRows.CalHoliday, OracleRows.CalSpecial],
        [OracleRows.X6, OracleRows.X9, OracleRows.Pending],
        [OracleRows.E8],
        [OracleRows.X8Due, OracleRows.X8Last],
        [OracleRows.D11Close, OracleRows.D11EndOfData],
    ];

    private static readonly string[] Names =
    [
        "gap above the limit (E5)",
        "gap through the stop at entry (E6)",
        "stop gap exit",
        "target gap exit",
        "stop and target in one minute, open between",
        "stop and target in one minute, open beyond the target",
        "holes before the fill minute",
        "holes inside the position",
        "hole at the due minute or the square-off minute",
        "ties and the grid",
        "zero-volume minutes",
        "special session or holiday with a calendar",
        "end of data inside a position, order pending",
        "overnight orders",
        "MaxHold at the close of session N",
        "the gate",
    ];

    /// <summary>The number of categories.</summary>
    public static int Count => Names.Length;

    /// <summary>The name of a category (1 to 16).</summary>
    public static string Name(int category) => Names[category - 1];

    /// <summary>The marker rows of a category (1 to 16): reaching any of them counts.</summary>
    public static IReadOnlyList<string> MarkersOf(int category) => Markers[category - 1];

    /// <summary>True when the trace of a run reaches the category.</summary>
    public static bool Reached(int category, IReadOnlySet<string> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return Markers[category - 1].Any(rows.Contains);
    }
}
