namespace XDrishti.Domain.Learning;

/// <summary>What the frozen-model counterfactual concludes (P6-12). "No difference" is an ordinary answer.</summary>
public enum CounterfactualOutcome
{
    /// <summary>Too little history, too few judged windows, or a flag caps a difference: nothing can be said.</summary>
    Inconclusive,

    /// <summary>The retrained and the frozen model are not told apart out of sample.</summary>
    NoDifference,

    /// <summary>Continued learning scored better out of sample in enough windows and pooled.</summary>
    RetrainingHelps,

    /// <summary>The frozen model scored better out of sample in enough windows and pooled.</summary>
    FrozenBetter,
}
