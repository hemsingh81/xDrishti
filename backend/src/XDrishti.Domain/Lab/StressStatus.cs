namespace XDrishti.Domain.Lab;

/// <summary>The reading of one cost stress or delay gate (CF-05). <see cref="Unavailable"/> is never a pass: wherever a gate is read, it counts as a miss.</summary>
public enum StressStatus
{
    /// <summary>The figure meets its threshold.</summary>
    Pass = 0,

    /// <summary>The figure misses its threshold.</summary>
    Fail = 1,

    /// <summary>There is no figure to judge (no trades, a number that is not finite, a base that is not above zero) or the costs are not checked against a bill.</summary>
    Unavailable = 2,
}
