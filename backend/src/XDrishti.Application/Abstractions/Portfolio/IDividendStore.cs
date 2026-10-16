using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// What a dividend that is not voided is compared by when a file's row may be a second entry of it: its holding (null: unallocated), India pay date and rupees. For an unallocated one also what it says about the scrip - instrument, ISIN and ticker -
/// so that unrelated scrips paying the same rupees on the same day are not taken for each other; all three null means the dividend does not say, and it may be any scrip.
/// </summary>
public sealed record StoredDividendKey(Guid? HoldingId, DateOnly PayDate, decimal Amount, Guid? InstrumentId = null, string? Isin = null, string? Symbol = null);

/// <summary>
/// The dividends the owner entered (<c>portfolio.dividends</c>, ADR 0026). Immutable once stored except for being voided; the runtime role cannot delete them. New dividends and voids are saved by <see cref="IPortfolioUnitOfWork"/>.
/// </summary>
public interface IDividendStore
{
    /// <summary>One dividend, tracked so that voiding it can be saved; null when it does not exist.</summary>
    ValueTask<Dividend?> FindAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Collects the new dividend; <see cref="IPortfolioUnitOfWork"/> saves it.</summary>
    void Add(Dividend dividend);

    /// <summary>Collects the new dividends of a file; <see cref="IPortfolioUnitOfWork"/> saves them (an import of up to 20,000 rows is a few hundred batched INSERTs in the unit of work's transaction). A dividend whose identity is stored already (and not voided) makes the save fail.</summary>
    void AddRange(IEnumerable<Dividend> dividends);

    /// <summary>Which of these identities (<c>external_id</c>) the account has stored already among its dividends that are not voided; for reading only. A voided dividend's identity is free.</summary>
    ValueTask<IReadOnlySet<string>> StoredIdsAsync(Guid accountId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken);

    /// <summary>The dividends the owner typed in (source manual) that are not voided and were paid between the two dates (inclusive); for reading only. A row of a file equal to one of these is a possible duplicate.</summary>
    ValueTask<IReadOnlyList<StoredDividendKey>> ListManualActiveAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Voids every dividend of the batch that is not voided yet (one statement; executes at once, inside the current transaction); returns how many.</summary>
    ValueTask<int> VoidBatchAsync(Guid batchId, string by, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>True when the account already has a dividend that is not voided with the same holding (null: none), pay date and amount. For reading only.</summary>
    ValueTask<bool> ExistsSameAsync(Guid accountId, Guid? holdingId, DateOnly payDate, decimal amount, CancellationToken cancellationToken);

    /// <summary>One page of the dividends of a holding, voided ones included, newest first (pay date, then entered, descending). Total counts all of them.</summary>
    ValueTask<(IReadOnlyList<Dividend> Items, int Total)> ListForHoldingAsync(Guid holdingId, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>One page of the dividends of an account (every holding's and the unallocated ones), voided ones included, newest first. Total counts all of them.</summary>
    ValueTask<(IReadOnlyList<Dividend> Items, int Total)> ListForAccountAsync(Guid accountId, int page, int pageSize, CancellationToken cancellationToken);
}
