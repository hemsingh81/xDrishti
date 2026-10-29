using XDrishti.Domain.Planning;

namespace XDrishti.Application.Abstractions.Planning;

/// <summary>A link to store: a fill, what it is, and the plan it belongs to (P7-14, ADR 0093).</summary>
/// <param name="PlanId">The plan of the ticket; 0 for a discretionary fill.</param>
/// <param name="ForDate">The session the journal run was for.</param>
/// <param name="Link">The link.</param>
/// <param name="Uncertain">For an entry: why the position was already open before it (stored on the journal entry), else null.</param>
public sealed record NewJournalLink(long PlanId, DateOnly ForDate, JournalLink Link, string? Uncertain = null);

/// <summary>What a save wrote (rows that were already there are not counted).</summary>
/// <param name="Entries">New journal entries.</param>
/// <param name="Fills">New fill links.</param>
public sealed record JournalSaved(int Entries, int Fills);

/// <summary>A fill link as stored, with the trade it points at.</summary>
/// <param name="TransactionId">The stored trade.</param>
/// <param name="AccountId">The account.</param>
/// <param name="Symbol">The trading symbol.</param>
/// <param name="Side">buy or sell.</param>
/// <param name="Date">India date of the trade.</param>
/// <param name="Time">India time, when the source has one.</param>
/// <param name="TradeQty">The whole trade's shares.</param>
/// <param name="Price">The trade's price.</param>
/// <param name="Charges">The trade's recorded charges.</param>
/// <param name="Role">entry, exit or discretionary.</param>
/// <param name="Qty">Shares that belong to the role.</param>
/// <param name="Reason">Why.</param>
/// <param name="Live">True when the fill is a captured live fill (<see cref="TransactionId"/> is then the id of its <c>trading.live_fills</c> row).</param>
/// <param name="ChargesKnown">False when no charges were recorded for the fill.</param>
public sealed record StoredFill(Guid TransactionId, Guid AccountId, string Symbol, string Side, DateOnly Date, TimeOnly? Time, long TradeQty, decimal Price, decimal Charges, string Role, long Qty, string Reason, bool Live = false, bool ChargesKnown = true);

/// <summary>A journal entry (one ticket on one account) with its fills and its fill grade, if any.</summary>
/// <param name="Id">The entry row.</param>
/// <param name="PlanId">The plan.</param>
/// <param name="ItemId">The ticket.</param>
/// <param name="AccountId">The account.</param>
/// <param name="ForDate">The planned session.</param>
/// <param name="Fills">The entry and exit fills.</param>
/// <param name="Grade">The fill grade, or null while the position is open or the plan grade is not there yet.</param>
/// <param name="Uncertain">Set when the account already held the instrument before the entry: no R is written for this round trip.</param>
public sealed record StoredEntry(long Id, long PlanId, long ItemId, Guid AccountId, DateOnly ForDate, IReadOnlyList<StoredFill> Fills, FillGrade? Grade, string? Uncertain = null);

/// <summary>A fill grade to store.</summary>
/// <param name="EntryId">The journal entry.</param>
/// <param name="Grade">The grade.</param>
/// <param name="GradedAt">When.</param>
public sealed record NewFillGrade(long EntryId, FillGrade Grade, DateTimeOffset GradedAt);

/// <summary>
/// The auto-journal (<c>trading.journal_entries</c>, <c>journal_fills</c>, <c>fill_grades</c>), insert-only (P7-14, ADR 0093). It READS the fills the existing portfolio sync stored (<c>portfolio.transactions</c>, never written here)
/// and the intraday fills the live capture stored (<c>trading.live_fills</c>, P11-01b, ADR 0154: a delivery fill is never read from there, so no fill is journaled twice) and links them to tickets. Nothing here talks to a broker or places an order.
/// </summary>
public interface IJournalStore
{
    /// <summary>The broker fills (source broker, not voided) of Trading accounts and of accounts with an allocation, plus the non-delivery live fills of accounts with an allocation (flagged <see cref="JournalFill.Live"/>), from <paramref name="from"/> to <paramref name="to"/> inclusive, in a fixed order.</summary>
    ValueTask<IReadOnlyList<JournalFill>> FillsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Stores entries and fill links in one transaction; an entry or a fill that is already linked keeps its first link and is skipped (idempotent).</summary>
    ValueTask<JournalSaved> SaveAsync(IReadOnlyList<NewJournalLink> links, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>The account's net position (buys minus sells, any source, not voided) in each instrument before the given day, for each key. Missing keys are 0.</summary>
    ValueTask<IReadOnlyDictionary<(Guid Account, string Symbol, DateOnly Before), long>> PositionsBeforeAsync(IReadOnlyList<(Guid Account, string Symbol, DateOnly Before)> keys, CancellationToken cancellationToken);

    /// <summary>Entries without a fill grade, the NEWEST sessions first (so entries stuck for good cannot starve newer ones), at most <paramref name="limit"/>; returned oldest first.</summary>
    ValueTask<IReadOnlyList<StoredEntry>> OpenEntriesAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Stores fill grades; an entry that has one keeps it. Returns how many rows were written.</summary>
    ValueTask<int> InsertGradesAsync(IReadOnlyList<NewFillGrade> grades, CancellationToken cancellationToken);

    /// <summary>The entries of a plan with their fills and fill grades.</summary>
    ValueTask<IReadOnlyList<StoredEntry>> EntriesAsync(long planId, CancellationToken cancellationToken);

    /// <summary>The entries of several plans with their fills and fill grades, in one read (P8-04).</summary>
    ValueTask<IReadOnlyList<StoredEntry>> EntriesForPlansAsync(IReadOnlyList<long> planIds, CancellationToken cancellationToken);

    /// <summary>The discretionary fills journaled for a session.</summary>
    ValueTask<IReadOnlyList<StoredFill>> DiscretionaryAsync(DateOnly forDate, CancellationToken cancellationToken);
}
