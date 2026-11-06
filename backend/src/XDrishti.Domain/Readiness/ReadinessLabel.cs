namespace XDrishti.Domain.Readiness;

/// <summary>The four labels of the readiness score (spec CF-11 D4).</summary>
public enum ReadinessLabel
{
    /// <summary>Below the building cut-off, or any hard fail.</summary>
    NotReady = 0,

    /// <summary>From the building cut-off (40) to below the usable cut-off.</summary>
    Building = 1,

    /// <summary>From the usable cut-off (70) to below the ready cut-off.</summary>
    UsableWithAdvice = 2,

    /// <summary>From the ready cut-off (85).</summary>
    ReadyForNextPhase = 3,
}
