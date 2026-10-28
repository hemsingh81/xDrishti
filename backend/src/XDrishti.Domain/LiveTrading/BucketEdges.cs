namespace XDrishti.Domain.LiveTrading;

/// <summary>
/// The turnover edges (crore rupees of average daily turnover) between the liquidity buckets, strictly ascending, 3 to 6 values (spec P11-02, option <c>Live:Buckets:EdgesCr</c>, default 10, 50, 150, 500). A turnover at or above the top edge is L1; at or above the
/// next edge L2; and so on; below the lowest edge it is the last bucket. With four edges the buckets are L1 to L5. Printed on every report.
/// </summary>
public sealed record BucketEdges
{
    private BucketEdges(IReadOnlyList<decimal> edges) => EdgesCr = edges;

    /// <summary>The edges, ascending.</summary>
    public IReadOnlyList<decimal> EdgesCr { get; }

    /// <summary>The default edges of the spec.</summary>
    public static BucketEdges Default { get; } = new([10m, 50m, 150m, 500m]);

    /// <summary>The bucket words in use for these edges, most liquid first (L1 .. L{edges+1}), then <see cref="LiquidityBucket.Unknown"/>.</summary>
    public IReadOnlyList<string> Buckets => [.. Enumerable.Range(1, EdgesCr.Count + 1).Select(i => "L" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)), LiquidityBucket.Unknown];

    /// <summary>The edges when they are 3 to 6 strictly ascending positive values; null otherwise.</summary>
    public static BucketEdges? Create(IReadOnlyList<decimal> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        if (edges.Count is < 3 or > 6 || edges[0] <= 0)
        {
            return null;
        }

        for (var i = 1; i < edges.Count; i++)
        {
            if (edges[i] <= edges[i - 1])
            {
                return null;
            }
        }

        return new BucketEdges([.. edges]);
    }

    /// <summary>The bucket of a turnover in crore; <see cref="LiquidityBucket.Unknown"/> for a missing or non-positive turnover.</summary>
    public string Classify(decimal? turnoverCr)
    {
        if (turnoverCr is not { } t || t <= 0)
        {
            return LiquidityBucket.Unknown;
        }

        for (var i = EdgesCr.Count - 1; i >= 0; i--)
        {
            if (t >= EdgesCr[i])
            {
                return "L" + (EdgesCr.Count - i).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return "L" + (EdgesCr.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
