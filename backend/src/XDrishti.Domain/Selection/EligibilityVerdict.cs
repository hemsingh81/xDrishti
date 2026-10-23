namespace XDrishti.Domain.Selection;

/// <summary>What the filters say about one candidate as a whole.</summary>
public enum EligibilityVerdict
{
    /// <summary>Every required filter passed; optional filters may still be unavailable (caveats).</summary>
    Eligible,

    /// <summary>At least one filter failed.</summary>
    Rejected,

    /// <summary>No filter failed, but a required filter is unavailable: the candidate is held back, never passed on a guess.</summary>
    Held,
}
