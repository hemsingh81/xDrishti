using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// The ledger entries of the accounts (<c>portfolio.ledger_entries</c>, ADR 0025). Immutable and never deleted; an entry imported from a report (ADR 0029) is voided when its batch is undone, and a voided entry counts nowhere (not in lists, totals, the first date or the
/// identities that are taken). New entries are collected here and saved by <see cref="IPortfolioUnitOfWork"/>.
/// </summary>
public interface ILedgerStore
{
    /// <summary>Which of these identities the account has stored already. For reading only.</summary>
    ValueTask<IReadOnlySet<string>> StoredIdsAsync(Guid accountId, IReadOnlyCollection<string> externalIds, CancellationToken cancellationToken);

    /// <summary>Collects the new entries; <see cref="IPortfolioUnitOfWork"/> saves them. An entry whose identity is stored already makes the save fail.</summary>
    void Add(IEnumerable<LedgerEntry> entries);

    /// <summary>One page of the account's entries between the dates (inclusive; null is open) and of the class (null is all), newest first (date, then stored order, descending). Total counts all matches.</summary>
    ValueTask<(IReadOnlyList<LedgerEntry> Items, int Total)> ListAsync(Guid accountId, DateOnly? from, DateOnly? to, LedgerClass? entryClass, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>The charges Dhan's own ledger has for the account between the dates (inclusive), as the rupees paid (positive) on each day, one item per entry, each saying whether its text names a depository charge: what a depository charge of a downloaded sheet is compared with (see <see cref="LedgerSupersession.OpenCharges"/>).</summary>
    ValueTask<IReadOnlyList<LedgerChargeOnDay>> ListBrokerChargesAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Voids every entry of the import batch that is not voided yet (one statement; executes at once, inside the current transaction); returns how many.</summary>
    ValueTask<int> VoidBatchAsync(Guid batchId, string by, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>
    /// Voids, as superseded (ADR 0029), the imported charges that Dhan's own ledger now has: for each day and amount of <paramref name="brokerCharges"/> (charges the broker run has just collected, rupees paid) as many non-voided imported charge entries of the account with that day
    /// and amount, oldest first - one broker charge covers one imported line, and a day's amount of which the ledger has fewer charges than the sheet had lines leaves the rest counting. <paramref name="storedBefore"/> are the charges of the same days that earlier saves stored: they are
    /// not matched again, but they decide, with the new ones, which charges of a day count (a day with a depository charge anywhere in the stored day counts only those, <see cref="LedgerSupersession"/>). Executes at once, inside the current transaction; returns how many entries were voided.
    /// </summary>
    ValueTask<int> SupersedeImportedChargesAsync(Guid accountId, IReadOnlyCollection<LedgerChargeOnDay> brokerCharges, IReadOnlyCollection<LedgerChargeOnDay> storedBefore, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>The earliest date of any ledger entry Dhan's ledger gave for the account (whatever its class); null when there is none: the date the ledger is known from. Entries imported from reports do not count: they say nothing about how far Dhan's ledger was read.</summary>
    ValueTask<DateOnly?> FirstDateAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>The count and total amount per class between the dates (inclusive; null is open); classes without entries are absent.</summary>
    ValueTask<IReadOnlyList<LedgerTotal>> TotalsAsync(Guid accountId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken);
}

/// <summary>A charge of Dhan's ledger: its India date, the rupees paid (positive) and whether its text names it as a depository (DP) charge (<see cref="DepositoryCharges.Is"/>).</summary>
public sealed record LedgerChargeOnDay(DateOnly Date, decimal Paid, bool Depository = false);
