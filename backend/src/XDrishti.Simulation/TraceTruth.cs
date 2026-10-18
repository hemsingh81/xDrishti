namespace XDrishti.Simulation;

/// <summary>The truth of one condition at the decision step, as the trace of a candidate keeps it.</summary>
public enum TraceTruth
{
    /// <summary>The condition did not hold.</summary>
    False,

    /// <summary>The condition held.</summary>
    True,

    /// <summary>The condition could not be decided (a feature had no value yet, a lag the history cannot give).</summary>
    Unknown,
}
