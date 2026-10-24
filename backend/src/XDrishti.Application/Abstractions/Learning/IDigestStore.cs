using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>A digest to store (P7-15, ADR 0094).</summary>
/// <param name="Digest">The built digest.</param>
/// <param name="BuiltBy">Who or what built it (<c>system:weekly</c>, <c>cli:xd</c>).</param>
/// <param name="CreatedAt">When.</param>
public sealed record NewDigest(WeeklyDigest Digest, string BuiltBy, DateTimeOffset CreatedAt);

/// <summary>A stored digest.</summary>
/// <param name="Id">The row id.</param>
/// <param name="Digest">What was stored.</param>
/// <param name="BuiltBy">Who built it.</param>
/// <param name="CreatedAt">When.</param>
public sealed record StoredDigest(long Id, WeeklyDigest Digest, string BuiltBy, DateTimeOffset CreatedAt);

/// <summary>
/// The weekly digests (<c>research.digests</c>, P7-15, ADR 0094), insert-only, one per ISO week. This is the only write port the weekly service touches; nothing here applies a learning change or places an order.
/// </summary>
public interface IDigestStore
{
    /// <summary>Stores the digest unless its week has one; returns the stored one and whether this call wrote it (false when the week was already stored: the first digest is kept).</summary>
    ValueTask<(StoredDigest Stored, bool Created)> InsertAsync(NewDigest digest, CancellationToken cancellationToken);

    /// <summary>The digest of an ISO week (<c>2026-W43</c>), or null.</summary>
    ValueTask<StoredDigest?> FindByWeekAsync(string isoWeek, CancellationToken cancellationToken);

    /// <summary>The newest weeks first.</summary>
    ValueTask<IReadOnlyList<StoredDigest>> ListAsync(int limit, CancellationToken cancellationToken);
}
