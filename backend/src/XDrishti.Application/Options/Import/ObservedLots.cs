using XDrishti.Application.Abstractions.Options;

namespace XDrishti.Application.Options.Import;

/// <summary>
/// The lot the file shows for each underlying on the day (spec P14-01 criterion 4, ADR 0175): the lot of its nearest expiry on or after the day (during a lot
/// revision the far months already carry the new lot while the near ones keep the old, so the near one is what trades that day); at that expiry the most common
/// lot, ties to the smaller. An underlying with only expired rows shows none. The second value counts underlyings whose unexpired contracts carry more than one lot.
/// </summary>
internal static class ObservedLots
{
    public static (IReadOnlyList<LotObservation> Lots, int Mixed) Pick(IEnumerable<OptionContractCandidate> contracts, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(contracts);
        var lots = new List<LotObservation>();
        var mixed = 0;
        foreach (var underlying in contracts.Where(c => c.Expiry >= today).GroupBy(c => c.UnderlyingId).OrderBy(g => g.Key))
        {
            if (underlying.Select(c => c.LotSizeSeen).Distinct().Skip(1).Any())
            {
                mixed++;
            }

            var nearest = underlying.Min(c => c.Expiry);
            var lot = underlying.Where(c => c.Expiry == nearest).GroupBy(c => c.LotSizeSeen)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key;
            lots.Add(new LotObservation(underlying.Key, lot));
        }

        return (lots, mixed);
    }
}
