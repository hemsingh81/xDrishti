using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// The rows one answer of Dhan dropped, explained: the number per reason code and the index of the first <see cref="BrokerDrops.MaxLocations"/> of them in the answer. The reader adds the window and the page. Counts and indexes only.
/// </summary>
public sealed record BrokerReadDrops(IReadOnlyDictionary<string, int> ByReason, IReadOnlyList<BrokerRowDrop> Rows)
{
    public static BrokerReadDrops None { get; } = new(new Dictionary<string, int>(), []);

    /// <summary>The summary of every drop of an answer, in the order of the rows.</summary>
    public static BrokerReadDrops From(IReadOnlyList<BrokerRowDrop> drops)
    {
        ArgumentNullException.ThrowIfNull(drops);
        if (drops.Count == 0)
        {
            return None;
        }

        var byReason = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var drop in drops)
        {
            byReason[drop.Reason] = byReason.GetValueOrDefault(drop.Reason) + 1;
        }

        return new BrokerReadDrops(byReason, [.. drops.Take(BrokerDrops.MaxLocations)]);
    }
}
