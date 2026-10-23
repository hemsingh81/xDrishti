namespace XDrishti.Domain.Learning;

/// <summary>Where a cell is in its life (doc 09 section 7, spec P6-08). Every cell is <see cref="Research"/> until a changelog row says otherwise.</summary>
public enum CellState
{
    /// <summary>Being measured; the starting state of every cell.</summary>
    Research,

    /// <summary>Passed the out-of-sample gates; waits for the owner.</summary>
    Candidate,

    /// <summary>Approved to be followed on paper (shadow); inert until paper data exist (P10).</summary>
    Paper,

    /// <summary>Stayed within its band for the paper days; inert until live (P7+).</summary>
    Active,

    /// <summary>Stopped by drift or loss limits; still graded, may recover in shadow.</summary>
    Paused,

    /// <summary>Set aside; kept, never deleted, and final.</summary>
    Retired,
}
