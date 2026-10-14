using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>What the automatic rebuild after a trade sync did: holdings rebuilt, already right, left for the owner's review, and the others left alone (the first <see cref="BrokerSyncCounts.MaxNotRebuiltListed"/> listed, all counted).</summary>
internal sealed record BrokerRebuildSummary(int Rebuilt, int Unchanged, int NeedsReview, int NotRebuiltTotal, IReadOnlyList<BrokerNotRebuilt> NotRebuilt)
{
    public static BrokerRebuildSummary None { get; } = new(0, 0, 0, 0, []);

    public BrokerSyncCounts AddTo(BrokerSyncCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        return counts with { Rebuilt = Rebuilt, Unchanged = Unchanged, NeedsReview = NeedsReview, NotRebuiltTotal = NotRebuiltTotal, NotRebuilt = NotRebuilt };
    }
}
