namespace XDrishti.Simulation;

/// <summary>The three values of a condition at the moment of a decision (the logic of ADR 0034).</summary>
public enum TraceTruth
{
    /// <summary>The condition was false.</summary>
    False,

    /// <summary>The condition was true.</summary>
    True,

    /// <summary>The condition could not be decided (a value was not ready); never fires an entry.</summary>
    Unknown,
}
