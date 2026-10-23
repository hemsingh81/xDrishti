namespace XDrishti.Domain.Learning;

/// <summary>The stored text of a <see cref="CellState"/> (P6-08).</summary>
public static class CellStateExtensions
{
    /// <summary>The lower-case code kept in the database and shown in the API.</summary>
    public static string Code(this CellState state) => state switch
    {
        CellState.Research => "research",
        CellState.Candidate => "candidate",
        CellState.Paper => "paper",
        CellState.Active => "active",
        CellState.Paused => "paused",
        CellState.Retired => "retired",
        _ => throw new ArgumentOutOfRangeException(nameof(state)),
    };

    /// <summary>The state of a code, or null when it is none.</summary>
    public static CellState? Parse(string? code) => code switch
    {
        "research" => CellState.Research,
        "candidate" => CellState.Candidate,
        "paper" => CellState.Paper,
        "active" => CellState.Active,
        "paused" => CellState.Paused,
        "retired" => CellState.Retired,
        _ => null,
    };
}
