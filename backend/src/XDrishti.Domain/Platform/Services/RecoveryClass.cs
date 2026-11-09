namespace XDrishti.Domain.Platform.Services;

/// <summary>How a service can be made up after missed days (ADR 0240 §1). A service declares one explicitly; a test fails for a registered service that does not.</summary>
public enum RecoveryClass
{
    /// <summary>One run closes every missed day (the end-of-day run reads what is stored and fills the holes).</summary>
    Convergent,

    /// <summary>One run for the latest closed session; the older missed sessions are superseded by it.</summary>
    LatestOnly,

    /// <summary>One run per session, oldest first.</summary>
    Dated,

    /// <summary>Needs a live, decision-time observation: never made up later (ADR 0232), only reported as missed.</summary>
    TimeBound,
}
