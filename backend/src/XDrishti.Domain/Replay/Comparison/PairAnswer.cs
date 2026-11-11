namespace XDrishti.Domain.Replay.Comparison;

/// <summary>The answer of the frozen-versus-learning comparison (SR-01 AC 9).</summary>
public enum PairAnswer
{
    /// <summary>The learning run beat the frozen run with probability at or above the cut.</summary>
    LearningBetter,

    /// <summary>The frozen run beat the learning run with probability at or above the hurts bar.</summary>
    FrozenBetter,

    /// <summary>Neither side reached its bar.</summary>
    NoMeasurableDifference,

    /// <summary>Fewer paired days than the minimum, or fewer days with a non-zero difference than the minimum.</summary>
    TooLittleHistory,
}
