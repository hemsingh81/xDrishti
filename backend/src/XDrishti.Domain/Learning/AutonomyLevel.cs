namespace XDrishti.Domain.Learning;

/// <summary>The autonomy level of doc 09 section 13 / doc 16 <c>autonomy.level</c>. It is not stored anywhere yet (P8); callers state it.</summary>
public enum AutonomyLevel
{
    /// <summary>The system only advises.</summary>
    Advisory,

    /// <summary>The system proposes and the owner approves (the default).</summary>
    Supervised,

    /// <summary>The system may approve a hypothesis to be tried within guardrails. Accepting a verdict stays the owner's.</summary>
    Autonomous,
}
