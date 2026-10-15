namespace XDrishti.Domain.Portfolio;

/// <summary>
/// The rows a broker read dropped, explained: how many per reason code (<see cref="ByReason"/>; the reasons are a closed set of plain codes such as <c>missing_id</c> or <c>bad_quantity</c>) and the location of the first few
/// (<see cref="Rows"/>, at most <see cref="MaxLocations"/>). The sum of <see cref="ByReason"/> is <see cref="Total"/>, the number of rows dropped. Counts and locations only, never the content of a row.
/// </summary>
public sealed record BrokerDrops
{
    /// <summary>The most dropped rows whose location is kept (per run).</summary>
    public const int MaxLocations = 5;

    /// <summary>The reason of a drop that no mapper branch named.</summary>
    public const string Other = "other";

    public static BrokerDrops None { get; } = new();

    public IReadOnlyDictionary<string, int> ByReason { get; init; } = new Dictionary<string, int>();

    public IReadOnlyList<BrokerDroppedRow> Rows { get; init; } = [];

    public int Total => ByReason.Values.Sum();

    /// <summary>The figures of both, the locations of this one first and cut to <see cref="MaxLocations"/>.</summary>
    public BrokerDrops Plus(BrokerDrops other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new BrokerDrops
        {
            ByReason = Merge(ByReason, other.ByReason),
            Rows = [.. Rows.Concat(other.Rows).Take(MaxLocations)],
        };
    }

    /// <summary>These figures with <paramref name="count"/> more rows of the reason (no location).</summary>
    public BrokerDrops Plus(string reason, int count)
    {
        ArgumentException.ThrowIfNullOrEmpty(reason);
        return count <= 0 ? this : this with { ByReason = Merge(ByReason, new Dictionary<string, int> { [reason] = count }) };
    }

    /// <summary>These figures made to add up to <paramref name="dropped"/> rows: rows that were counted without a reason are <see cref="Other"/> (a source that only knows a number).</summary>
    public BrokerDrops Reconciled(int dropped) => Total < dropped ? Plus(Other, dropped - Total) : this;

    /// <summary>Merges two counts by reason into a dictionary sorted by reason (stable in the stored JSON).</summary>
    public static SortedDictionary<string, int> Merge(IReadOnlyDictionary<string, int> left, IReadOnlyDictionary<string, int> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var merged = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (reason, count) in left)
        {
            merged[reason] = merged.GetValueOrDefault(reason) + count;
        }

        foreach (var (reason, count) in right)
        {
            merged[reason] = merged.GetValueOrDefault(reason) + count;
        }

        return merged;
    }
}
