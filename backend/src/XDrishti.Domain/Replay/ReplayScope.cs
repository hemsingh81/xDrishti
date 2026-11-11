namespace XDrishti.Domain.Replay;

/// <summary>What a replay covers: baskets (members read as of each session) and individual instruments.</summary>
/// <param name="BasketIds">The chosen baskets.</param>
/// <param name="InstrumentIds">The chosen individual instruments.</param>
public sealed record ReplayScope(IReadOnlyList<Guid> BasketIds, IReadOnlyList<Guid> InstrumentIds)
{
    /// <summary>Whether nothing is chosen.</summary>
    public bool IsEmpty => BasketIds.Count == 0 && InstrumentIds.Count == 0;
}
