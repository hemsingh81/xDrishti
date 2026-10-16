using XDrishti.Domain.Common;

namespace XDrishti.Domain.Portfolio;

/// <summary>
/// The owner's decision about a ledger entry of class <c>dividend</c> (ADR 0026): accepted (a dividend was created from it, <see cref="DividendId"/>) or dismissed. A ledger entry without a row here is still a suggestion. The decision is final and the row
/// is never deleted: a dividend that is voided later does not bring the suggestion back. The ledger entry is the identity (one decision per entry).
/// </summary>
public sealed class DividendSuggestion : Entity<Guid>
{
    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private DividendSuggestion(Guid id, Guid accountId, DividendSuggestionState state, Guid? dividendId, string decidedBy, DateTimeOffset decidedAt)
        : base(id)
    {
        AccountId = accountId;
        State = state;
        DividendId = dividendId;
        DecidedBy = decidedBy;
        DecidedAt = decidedAt;
    }

    /// <summary>The account of the ledger entry.</summary>
    public Guid AccountId { get; private set; }

    public DividendSuggestionState State { get; private set; }

    /// <summary>The dividend created from the entry; null when it was dismissed.</summary>
    public Guid? DividendId { get; private set; }

    public string DecidedBy { get; private set; }

    public DateTimeOffset DecidedAt { get; private set; }

    /// <summary>The decision for the ledger entry <paramref name="ledgerEntryId"/> (which is this row's id): accepted as the dividend <paramref name="dividendId"/>.</summary>
    public static DividendSuggestion Accept(Guid ledgerEntryId, Guid accountId, Guid dividendId, string by, DateTimeOffset now) =>
        Create(ledgerEntryId, accountId, DividendSuggestionState.Accepted, dividendId, by, now);

    /// <summary>The decision for the ledger entry <paramref name="ledgerEntryId"/>: dismissed.</summary>
    public static DividendSuggestion Dismiss(Guid ledgerEntryId, Guid accountId, string by, DateTimeOffset now) =>
        Create(ledgerEntryId, accountId, DividendSuggestionState.Dismissed, null, by, now);

    private static DividendSuggestion Create(Guid ledgerEntryId, Guid accountId, DividendSuggestionState state, Guid? dividendId, string by, DateTimeOffset now)
    {
        if (ledgerEntryId == Guid.Empty || accountId == Guid.Empty)
        {
            throw new ArgumentException("A decision is about a ledger entry of an account.", nameof(ledgerEntryId));
        }

        var actor = by?.Trim();
        actor = string.IsNullOrEmpty(actor) ? "unknown" : actor.Length <= Dividend.MaxCreatedByLength ? actor : actor[..Dividend.MaxCreatedByLength];
        return new DividendSuggestion(ledgerEntryId, accountId, state, dividendId, actor, now);
    }
}
