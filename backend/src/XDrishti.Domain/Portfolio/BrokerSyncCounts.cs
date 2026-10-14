namespace XDrishti.Domain.Portfolio;

/// <summary>
/// What a broker history run did (ADR 0025), kept as JSON on the run. <see cref="WindowsDone"/> of <see cref="WindowsTotal"/> date windows are read and stored; <see cref="Halved"/> windows were split in half because Dhan refused them as too large or answered "no data" for a stretch of several dates (the halves settle which).
/// <see cref="Read"/> rows came from Dhan, <see cref="Stored"/> are new, <see cref="Duplicates"/> were stored already, <see cref="Dropped"/> were unusable rows, <see cref="Enriched"/> of the <see cref="Duplicates"/> were completed (a trade stored by an earlier read without its ISIN or charges got them from this one; nothing else of it is touched) and <see cref="Skipped"/> were not NSE equity trades (by reason in
/// <see cref="SkippedByReason"/>). After a trade run the automatic rebuild adds <see cref="Rebuilt"/> holdings rebuilt, <see cref="Unchanged"/> already right, <see cref="NeedsReview"/> holdings with a hand-dated lot that were left alone and the
/// first <see cref="MaxNotRebuiltListed"/> of the holdings or securities that were left alone with their reason (<see cref="NotRebuiltTotal"/> counts all of them). <see cref="EmptyByNoData"/> counts the windows (or half-windows) that were taken as
/// empty only because Dhan answered DH-907 ("incorrect parameters or no data present") to the first page: that code means both "nothing there" and "bad request", so the owner can see how often an empty stretch rested on it alone. Counts only: no trade or ledger content.
/// </summary>
public sealed record BrokerSyncCounts
{
    /// <summary>The most holdings or securities listed in <see cref="NotRebuilt"/>.</summary>
    public const int MaxNotRebuiltListed = 50;

    public static BrokerSyncCounts None { get; } = new();

    public int WindowsDone { get; init; }

    public int WindowsTotal { get; init; }

    public int Halved { get; init; }

    public int Read { get; init; }

    public int Stored { get; init; }

    public int Duplicates { get; init; }

    public int Dropped { get; init; }

    /// <summary>Trades stored already (counted in <see cref="Duplicates"/> too) that got an ISIN, charges or ticker they lacked from this read.</summary>
    public int Enriched { get; init; }

    public int Skipped { get; init; }

    /// <summary>Windows (or half-windows) taken as empty only because Dhan answered DH-907 to the first page.</summary>
    public int EmptyByNoData { get; init; }

    public IReadOnlyDictionary<string, int> SkippedByReason { get; init; } = new Dictionary<string, int>();

    public int Rebuilt { get; init; }

    public int Unchanged { get; init; }

    public int NeedsReview { get; init; }

    public int NotRebuiltTotal { get; init; }

    public IReadOnlyList<BrokerNotRebuilt> NotRebuilt { get; init; } = [];

    /// <summary>Adds the figures of one window read.</summary>
    public BrokerSyncCounts AddWindow(int read, int stored, int duplicates, int dropped, IReadOnlyDictionary<string, int> skipped, bool halved, int emptyByNoData = 0, int enriched = 0)
    {
        ArgumentNullException.ThrowIfNull(skipped);
        var merged = new SortedDictionary<string, int>(SkippedByReason.ToDictionary(p => p.Key, p => p.Value), StringComparer.Ordinal);
        foreach (var (reason, count) in skipped)
        {
            merged[reason] = merged.GetValueOrDefault(reason) + count;
        }

        return this with
        {
            WindowsDone = WindowsDone + 1,
            Halved = Halved + (halved ? 1 : 0),
            Read = Read + read,
            Stored = Stored + stored,
            Duplicates = Duplicates + duplicates,
            Dropped = Dropped + dropped,
            Enriched = Enriched + enriched,
            EmptyByNoData = EmptyByNoData + emptyByNoData,
            Skipped = merged.Values.Sum(),
            SkippedByReason = merged,
        };
    }
}
