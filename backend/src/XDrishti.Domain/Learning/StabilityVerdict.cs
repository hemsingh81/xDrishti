namespace XDrishti.Domain.Learning;

/// <summary>Whether a feature's direction of effect holds from fold to fold (CF-10 part 10b, decision D3).</summary>
public enum StabilityVerdict
{
    /// <summary>The direction flips in no more than the allowed share of the folds that give one.</summary>
    Stable = 0,

    /// <summary>The direction flips in more than the allowed share: the feature would be dropped.</summary>
    Unstable = 1,

    /// <summary>Fewer than two folds give a direction (no explanation, too few rows, no variation): nothing can be said, and that is never read as stable.</summary>
    Unavailable = 2,
}
