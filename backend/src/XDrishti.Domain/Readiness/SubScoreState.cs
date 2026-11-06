namespace XDrishti.Domain.Readiness;

/// <summary>Whether a sub-score has a number (spec CF-11 assumption 3).</summary>
public enum SubScoreState
{
    /// <summary>The sub-score has a number between 0 and 100.</summary>
    Scored = 0,

    /// <summary>The sub-score does not apply (no ML model: no calibration). It is left out of the minimum and says so.</summary>
    NotApplicable = 1,

    /// <summary>The sub-score applies but its input does not exist. It counts as a hard fail, never a pass.</summary>
    Unavailable = 2,
}
