namespace XDrishti.Domain.Options.Costs;

/// <summary>
/// A stored half-spread model loaded for lookups (spec P14-07): one underlying, the sessions it rests on, and its bucket rows. A lookup is a dictionary read. The bucket asked for is answered by its own row; else by the row with the time-of-day split merged;
/// else by the row with the moneyness split merged too; else it is <see langword="null"/>: unavailable, never a number.
/// </summary>
public sealed class HalfSpreadModel
{
    private readonly Dictionary<(string, string, string), SpreadBucketRow> _rows;

    public HalfSpreadModel(string modelId, Guid underlyingId, int sessions, DateOnly firstSession, DateOnly lastSession, IEnumerable<SpreadBucketRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ModelId = modelId;
        UnderlyingId = underlyingId;
        Sessions = sessions;
        FirstSession = firstSession;
        LastSession = lastSession;
        _rows = rows.ToDictionary(r => (r.Moneyness, r.Dte, r.Tod));
    }

    public string ModelId { get; }

    public Guid UnderlyingId { get; }

    public int Sessions { get; }

    public DateOnly FirstSession { get; }

    public DateOnly LastSession { get; }

    public int BucketCount => _rows.Count;

    /// <summary>The row that answers the bucket, or null when the bucket and both merged pools were short of quotes.</summary>
    public SpreadBucketRow? Lookup(SpreadBucket bucket)
    {
        ArgumentNullException.ThrowIfNull(bucket);
        if (_rows.TryGetValue((bucket.Moneyness, bucket.Dte, bucket.Tod), out var own))
        {
            return own;
        }

        if (_rows.TryGetValue((bucket.Moneyness, bucket.Dte, SpreadBuckets.All), out var timeMerged))
        {
            return timeMerged;
        }

        return _rows.TryGetValue((SpreadBuckets.All, bucket.Dte, SpreadBuckets.All), out var allMerged) ? allMerged : null;
    }
}
