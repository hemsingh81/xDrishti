namespace XDrishti.Domain.Learning;

/// <summary>How a probability of beating the rung below reads (CF-10 part 10c-1, spec D4): a pass needs the bar, a probability between the "promising" level and the bar is never a pass.</summary>
public enum PairedReading
{
    /// <summary>Below the "promising" level.</summary>
    Below = 0,

    /// <summary>From the "promising" level and under the pass bar: shown, never a pass.</summary>
    Promising = 1,

    /// <summary>At or above the pass bar.</summary>
    Pass = 2,
}
