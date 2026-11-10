namespace XDrishti.Domain.Market.Baskets;

/// <summary>What a new list changes: the instruments to add (in the list's order) and to remove.</summary>
public sealed record MembershipChange(IReadOnlyList<Guid> Added, IReadOnlyList<Guid> Removed)
{
    /// <summary>The size of the change: the larger of the two sides (a swap of two names is a change of 2).</summary>
    public int Size => Math.Max(Added.Count, Removed.Count);
}

/// <summary>The difference between the current members and the incoming list; pure.</summary>
public static class MembershipDiff
{
    public static MembershipChange Compute(IReadOnlyCollection<Guid> current, IReadOnlyList<Guid> incoming)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(incoming);
        var now = current.ToHashSet();
        var next = incoming.ToHashSet();
        return new MembershipChange([.. incoming.Distinct().Where(id => !now.Contains(id))], [.. current.Where(id => !next.Contains(id))]);
    }
}
