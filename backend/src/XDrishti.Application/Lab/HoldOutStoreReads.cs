using XDrishti.Application.Abstractions.Lab;

namespace XDrishti.Application.Lab;

/// <summary>Reads of the hold-out chain that more than one slice needs (HO-01).</summary>
internal static class HoldOutStoreReads
{
    /// <summary>The released blocks as (start, end) pairs, earliest first, for the <c>seen_holdout</c> labels (D7); none until the first release.</summary>
    public static async ValueTask<IReadOnlyList<(DateOnly Start, DateOnly End)>> ReleasedAsync(this IHoldOutStore store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        return [.. (await store.ListBlocksAsync(cancellationToken)).Where(b => b.Release is not null).Select(b => (b.Block.Start, b.Block.HistoryLast))];
    }
}
