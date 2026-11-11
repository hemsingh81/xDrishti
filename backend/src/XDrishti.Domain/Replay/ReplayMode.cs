namespace XDrishti.Domain.Replay;

/// <summary>Whether the cell statistics and cell states are held at the replay start or recomputed as the replay goes (SR-01).</summary>
public enum ReplayMode
{
    /// <summary>Cell statistics and states as of the start date, for the whole run.</summary>
    Frozen,

    /// <summary>Cell statistics recomputed in memory as of each session on the weekly cadence; never stored.</summary>
    Learning,
}
