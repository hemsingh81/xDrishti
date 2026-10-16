using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// The trades of the accounts (<c>portfolio.transactions</c>, ADR 0023). Trades are immutable once stored except for being voided; the runtime role cannot delete them. New trades and voids are saved by
/// <see cref="IPortfolioUnitOfWork"/>; <see cref="VoidBatchAsync"/> executes at once, inside the current transaction.
/// </summary>
public interface ITradeStore
{
    /// <summary>Every trade of the account that is not voided, oldest stored first; for reading only (not tracked).</summary>
    ValueTask<IReadOnlyList<Transaction>> ListActiveAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>
    /// The trades of a security of an account that are not voided, oldest stored first; for reading only. The same rule as <see cref="ListForSecurityAsync"/> (ISIN, or symbol when either side has no ISIN): the caller
    /// keeps the ones its holding matcher resolves to the holding. Reads one security's trades, not the account's whole history.
    /// </summary>
    ValueTask<IReadOnlyList<Transaction>> ListActiveForSecurityAsync(Guid accountId, string? isin, string symbol, CancellationToken cancellationToken);

    /// <summary>The securities (ISIN and symbol, each pair once, ordered) of the account's broker trades that are not voided: what the automatic rebuild after a broker sync needs to find the holdings to rebuild, without reading every trade.</summary>
    ValueTask<IReadOnlyList<TradeSecurity>> ListBrokerSecuritiesAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>Which of these identities (<c>external_id</c>) the account has stored already among its trades that are not voided; for reading only. A voided trade's identity is free.</summary>
    ValueTask<IReadOnlySet<string>> StoredIdsAsync(Guid accountId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken);

    /// <summary>
    /// Among these identities, the account's trades that came from the broker, are not voided and lack something a read of the broker can give (no ISIN, no ticker, or charges of 0); tracked, so that
    /// <see cref="Transaction.EnrichFromBroker"/> can be saved by <see cref="IPortfolioUnitOfWork"/>.
    /// </summary>
    ValueTask<IReadOnlyList<Transaction>> ListBrokerAwaitingEnrichmentAsync(Guid accountId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken);

    /// <summary>The trades of the batch that are not voided; for reading only.</summary>
    ValueTask<IReadOnlyList<Transaction>> ListActiveOfBatchAsync(Guid batchId, CancellationToken cancellationToken);

    /// <summary>One trade, tracked so that voiding it can be saved; null when it does not exist.</summary>
    ValueTask<Transaction?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// One page of the trades of a security of an account — voided ones included — newest first (trade date, time, then stored order, descending). A trade belongs to the security when its ISIN is the given one, or when its symbol is the given
    /// one and either ISIN is missing. <paramref name="isin"/> may be null (a holding without ISIN).
    /// </summary>
    ValueTask<(IReadOnlyList<Transaction> Items, int Total)> ListForSecurityAsync(Guid accountId, string? isin, string symbol, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>
    /// The groups of the broker's own trades (source <c>broker</c>, not voided) between the dates (inclusive), one per India day, ISIN and side with the total quantity; trades without an ISIN have no group (ADR 0029: what a bill-level row of a downloaded report is compared with).
    /// </summary>
    ValueTask<IReadOnlyList<TradeGroup>> ListBrokerGroupsAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>
    /// Voids, as superseded by <paramref name="runId"/>, every bill-level row (source <c>bill</c>, not voided) of the account that falls in one of these groups - same day, ISIN and side as trades the broker run has just stored (ADR 0029). Executes at once, inside the
    /// current transaction. The outcome says how many rows were voided and in how many groups the bill rows' quantity differed from the broker's.
    /// </summary>
    ValueTask<SupersedeOutcome> SupersedeBillRowsAsync(Guid accountId, IReadOnlyCollection<TradeGroup> brokerGroups, Guid runId, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Collects the new trades; <see cref="IPortfolioUnitOfWork"/> saves them (an import of up to 20,000 rows is a few hundred batched INSERTs in the unit of work's transaction; COPY would need its own connection outside it). A trade whose identity is stored already (and not voided) makes the save fail.</summary>
    void Add(IEnumerable<Transaction> trades);

    /// <summary>Voids every trade of the batch that is not voided yet (one statement; executes at once, inside the current transaction); returns how many.</summary>
    ValueTask<int> VoidBatchAsync(Guid batchId, string by, DateTimeOffset at, CancellationToken cancellationToken);
}

/// <summary>The trades of one India day, ISIN and side with their total quantity.</summary>
public sealed record TradeGroup(DateOnly Date, string Isin, TradeSide Side, long Qty);

/// <summary>What superseding bill-level rows did: the rows voided and the groups among them whose total quantity differed from the broker's.</summary>
public sealed record SupersedeOutcome(int Rows, int Differing)
{
    public static SupersedeOutcome None { get; } = new(0, 0);

    /// <summary>The two outcomes together.</summary>
    public SupersedeOutcome Plus(SupersedeOutcome other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new(Rows + other.Rows, Differing + other.Differing);
    }
}
