using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>The ledger entries of the accounts (<c>portfolio.ledger_entries</c>, ADR 0025). Immutable and never deleted. New entries are collected here and saved by <see cref="IPortfolioUnitOfWork"/>.</summary>
public interface ILedgerStore
{
    /// <summary>Which of these identities the account has stored already. For reading only.</summary>
    ValueTask<IReadOnlySet<string>> StoredIdsAsync(Guid accountId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken);

    /// <summary>Collects the new entries; <see cref="IPortfolioUnitOfWork"/> saves them. An entry whose identity is stored already makes the save fail.</summary>
    void Add(IEnumerable<LedgerEntry> entries);

    /// <summary>One page of the account's entries between the dates (inclusive; null is open) and of the class (null is all), newest first (date, then stored order, descending). Total counts all matches.</summary>
    ValueTask<(IReadOnlyList<LedgerEntry> Items, int Total)> ListAsync(Guid accountId, DateOnly? from, DateOnly? to, LedgerClass? entryClass, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>The earliest date of any ledger entry stored for the account (whatever its class); null when there is none: the date the ledger is known from.</summary>
    ValueTask<DateOnly?> FirstDateAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>The count and total amount per class between the dates (inclusive; null is open); classes without entries are absent.</summary>
    ValueTask<IReadOnlyList<LedgerTotal>> TotalsAsync(Guid accountId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken);
}
