namespace XDrishti.Domain.Lab;

/// <summary>The reading of a chance test (CF-07): <see cref="Unavailable"/> is never a pass.</summary>
public enum NullStatus
{
    /// <summary>The test could not be made; for any consumer this is a fail.</summary>
    Unavailable = 0,

    /// <summary>The real mean is not explained by random entries at the level of the test.</summary>
    Pass = 1,

    /// <summary>Random entries with the same exits and costs do as well as the strategy.</summary>
    Fail = 2,
}
