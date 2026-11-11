namespace XDrishti.Domain.Replay;

/// <summary>Which strategy versions a replay uses (SR-01, Q1): the version active at the replay start for the whole run, or for each session the latest version written by then.</summary>
public enum ReplayVersionRule
{
    /// <summary>Today's active version of each strategy, run backwards over the range: labelled hindsight.</summary>
    Start,

    /// <summary>The latest version whose creation time is at or before the close of the session; none means the cell is blocked as <c>no_version</c>.</summary>
    AsOf,
}
