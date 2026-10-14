using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Portfolio.Groups;

/// <summary>One group before its figures: the holdings that belong to it.</summary>
internal sealed record GroupSlice(string Key, string Name, IReadOnlyList<HoldingData> Holdings);

/// <summary>
/// Splits holdings into groups (ADR 0021). Pure: no I/O. Accounts, buckets and sectors give every holding exactly one group; a basket grouping puts a holding into every live basket that holds its instrument and into "No basket" when
/// there is none. The sector is the master's; a holding without an instrument or without a sector is "Unclassified". The key of those two is <see cref="NoneKey"/>, which no real group key can be: ids are Guids, and a master sector spelled like it (any case) is treated as Unclassified.
/// </summary>
internal static class HoldingPartitions
{
    /// <summary>The group key of "Unclassified" (sector) and "No basket".</summary>
    public const string NoneKey = "~none";

    public const string Unclassified = "Unclassified";

    public const string NoBasket = "No basket";

    /// <summary>The key of the extra row that holds the dividends and costs of no holding (ADR 0026).</summary>
    public const string UnallocatedKey = "~unallocated";

    public const string Unallocated = "Unallocated";

    public static IReadOnlyList<GroupSlice> Partition(IEnumerable<HoldingData> holdings, GroupDimension dimension, Classification classification)
    {
        ArgumentNullException.ThrowIfNull(holdings);
        ArgumentNullException.ThrowIfNull(classification);
        var order = new List<string>();
        var labels = new Dictionary<string, (string Key, string Name)>(StringComparer.OrdinalIgnoreCase);
        var members = new Dictionary<string, List<HoldingData>>(StringComparer.OrdinalIgnoreCase);
        foreach (var holding in holdings)
        {
            foreach (var (key, name) in Labels(holding, dimension, classification))
            {
                if (!members.TryGetValue(key, out var list))
                {
                    members[key] = list = [];
                    labels[key] = (key, name);
                    order.Add(key);
                }
                else if (Compare((key, name), labels[key]) < 0)
                {
                    // Keys are compared ignoring case (the master may spell a sector two ways); the spelling shown is the Ordinal-smallest one, whatever order the holdings were read in.
                    labels[key] = (key, name);
                }

                list.Add(holding);
            }
        }

        return [.. order.Select(key => new GroupSlice(labels[key].Key, labels[key].Name, members[key]))];
    }

    /// <summary>True when the holding is in the group with this key (compared ignoring case); false for a key that names no group.</summary>
    public static bool InGroup(HoldingData holding, GroupDimension dimension, string key, Classification classification)
    {
        ArgumentNullException.ThrowIfNull(holding);
        return Labels(holding, dimension, classification).Any(l => string.Equals(l.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private static int Compare((string Key, string Name) a, (string Key, string Name) b)
    {
        var byKey = string.CompareOrdinal(a.Key, b.Key);
        return byKey != 0 ? byKey : string.CompareOrdinal(a.Name, b.Name);
    }

    private static IEnumerable<(string Key, string Name)> Labels(HoldingData holding, GroupDimension dimension, Classification classification)
    {
        switch (dimension)
        {
            case GroupDimension.Account:
                yield return (holding.AccountId.ToString(), holding.AccountName);
                break;
            case GroupDimension.Bucket:
                yield return (holding.BucketId.ToString(), holding.BucketName);
                break;
            case GroupDimension.Sector:
                // A sector that the master spells like the reserved key (any case) is deliberately Unclassified: it must not become a group that the key "~none" cannot tell apart.
                yield return holding.InstrumentId is { } instrument
                    && classification.Sectors.TryGetValue(instrument, out var sector)
                    && !string.Equals(sector, NoneKey, StringComparison.OrdinalIgnoreCase)
                    ? (sector, sector)
                    : (NoneKey, Unclassified);
                break;
            case GroupDimension.Basket:
                if (holding.InstrumentId is { } member && classification.Baskets.TryGetValue(member, out var baskets) && baskets.Count > 0)
                {
                    foreach (var basket in baskets)
                    {
                        yield return (basket.Id.ToString(), basket.Name);
                    }
                }
                else
                {
                    yield return (NoneKey, NoBasket);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(dimension));
        }
    }
}
