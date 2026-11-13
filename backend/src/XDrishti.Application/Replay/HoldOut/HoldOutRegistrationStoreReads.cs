using XDrishti.Application.Abstractions.Replay;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>Reads over <see cref="IHoldOutRegistrationStore"/>.</summary>
internal static class HoldOutRegistrationStoreReads
{
    /// <summary>The newest registration that targets the block, or null (D2: it is the one; older ones read superseded).</summary>
    public static async ValueTask<StoredHoldOutRegistration?> NewestForBlockAsync(this IHoldOutRegistrationStore store, DateOnly blockStart, CancellationToken cancellationToken)
    {
        var rows = await store.ForBlockAsync(blockStart, cancellationToken);
        return rows.Count == 0 ? null : rows[0];
    }
}
