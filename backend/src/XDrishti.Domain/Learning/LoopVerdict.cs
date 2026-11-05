namespace XDrishti.Domain.Learning;

/// <summary>How an arm comparison of the loop test reads (spec CF-12, D4). "No measurable difference" is an answer, not a failure.</summary>
public enum LoopVerdict
{
    /// <summary>An input is missing (no scores, no base rate) or not usable. The unset state.</summary>
    Unavailable = 0,

    /// <summary>Fewer distinct days than the test needs.</summary>
    Inconclusive = 1,

    /// <summary>The first arm is better with probability at or above the bar.</summary>
    Helps = 2,

    /// <summary>The first arm is worse with probability at or above the bar.</summary>
    Hurts = 3,

    /// <summary>Neither.</summary>
    NoMeasurableDifference = 4,
}
