using XDrishti.Application.Abstractions.Replay;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>Reads over <see cref="IHoldOutRegistrationStore"/>.</summary>
internal static class HoldOutRegistrationStoreReads
{
    /// <summary>
    /// The newest registration that targets the block and was stored before the block began (D2: it is the one; older ones read superseded), or null. A registration stored on or after the start can never take a fresh look (HO-01 D5), so it is not the one and holds nothing.
    /// </summary>
    public static async ValueTask<StoredHoldOutRegistration?> NewestForBlockAsync(this IHoldOutRegistrationStore store, DateOnly blockStart, CancellationToken cancellationToken)
    {
        var rows = await store.ForBlockAsync(blockStart, cancellationToken);
        return rows.FirstOrDefault(r => IndiaTime.Today(r.CreatedAt) < blockStart);
    }
}
