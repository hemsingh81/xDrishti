namespace XDrishti.Simulation;

/// <summary>
/// Why a decision was taken: the conditions and the values they read at that moment. Filled by the entry scanner of P3-03a; the trade walker never reads it, so that it can be empty in a hand-made candidate.
/// </summary>
/// <param name="Items">The conditions, in the order of the strategy.</param>
public sealed record DecisionTrace(IReadOnlyList<TraceItem> Items)
{
    /// <summary>A trace with no items.</summary>
    public static DecisionTrace Empty { get; } = new([]);
}
