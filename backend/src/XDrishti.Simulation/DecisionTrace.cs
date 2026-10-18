namespace XDrishti.Simulation;

/// <summary>Which conditions were true, false or unknown at the decision bar, and the values they read (doc 09 §3 step 3 in miniature). Filled by the entry scanner; the walker never reads it.</summary>
public sealed record DecisionTrace(IReadOnlyList<TraceItem> Items)
{
    /// <summary>A trace with no items (a hand-made candidate).</summary>
    public static DecisionTrace Empty { get; } = new([]);
}
