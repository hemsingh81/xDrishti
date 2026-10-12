using XDrishti.Domain.Market.Bars;

namespace XDrishti.Domain.Market.Quality;

/// <summary>
/// One bucket, stored and recomputed side by side. <see cref="Fields"/> lists what differs (<c>open high low close volume bars</c>), <c>missing</c> when the 1-minute bars give a bucket that is not stored, or
/// <c>unexpected</c> when a bucket is stored that no 1-minute bar belongs to; empty when they are equal.
/// </summary>
public sealed record BucketComparison(Timeframe Timeframe, long Start, StoredBucket? Stored, AggBucket? Recomputed, IReadOnlyList<string> Fields)
{
    public bool Differs => Fields.Count > 0;

    public DateTimeOffset Ts => IndiaTime.ToIst(DateTimeOffset.FromUnixTimeSeconds(Start));
}
