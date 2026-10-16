namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>The ledger dividends of an account that wait for the owner's decision: how many, and the id of the newest (the item changes when a new one arrives).</summary>
public sealed record PendingDividends(int Count, Guid NewestId);

/// <summary>Securities of an account that have no match in the instrument master: open holdings with no instrument and distinct securities of the broker's trades with no instrument.</summary>
public sealed record UnmatchedCounts(int Holdings, int TradeSecurities)
{
    public int Total => Holdings + TradeSecurities;
}

/// <summary>What a holding is now, for the items that were worked out from the last run of the trade sync: whether it is open and whether it still has a lot whose date the owner chose.</summary>
public sealed record HoldingFlags(bool Open, bool HandDated);

/// <summary>The set-based reads behind the attention items (P2-16, ADR 0027): each answers for all the given accounts or holdings at once, never one by one.</summary>
public interface IAttentionQueries
{
    /// <summary>The ledger dividends of each account that wait for the owner's decision (accepted or dismissed ones, and amounts that cannot be entered, are not counted); accounts with none are absent.</summary>
    ValueTask<IReadOnlyDictionary<Guid, PendingDividends>> PendingDividendsAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken);

    /// <summary>Unmatched securities of each account; accounts with none are absent. Securities of trades count only when traded on or after <paramref name="tradedSince"/> (an old delisted share that never matched is not news).</summary>
    ValueTask<IReadOnlyDictionary<Guid, UnmatchedCounts>> UnmatchedAsync(IReadOnlyCollection<Guid> accountIds, DateOnly tradedSince, CancellationToken cancellationToken);

    /// <summary>The current flags of the given holdings; a holding that does not exist is absent.</summary>
    ValueTask<IReadOnlyDictionary<Guid, HoldingFlags>> HoldingFlagsAsync(IReadOnlyCollection<Guid> holdingIds, CancellationToken cancellationToken);
}
