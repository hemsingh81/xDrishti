namespace XDrishti.Domain.Readiness;

/// <summary>The reading of one hard-fail term (spec CF-11 D3).</summary>
public enum FaultState
{
    /// <summary>The input does not exist. For a term that applies this is a hard fail, never a pass.</summary>
    Unavailable = 0,

    /// <summary>The fault is not there.</summary>
    Clear = 1,

    /// <summary>The fault is there: a hard fail.</summary>
    Fault = 2,

    /// <summary>The term does not apply yet (it has no producer by design). It is listed and is not a hard fail.</summary>
    NotApplicable = 3,
}
