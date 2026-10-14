using XDrishti.Application.Portfolio.BrokerHistory;

namespace XDrishti.Application.Portfolio.Dividends;

// Request and response shapes of the dividends API (ADR 0026, P2-14). Dates are India dates, amounts rupees.

/// <summary>
/// A dividend typed in by the owner: for a holding (<c>HoldingId</c>; the account is then the holding's) or, left unallocated, for an account (<c>AccountId</c> alone). <c>PayDate</c> is the day the money was credited (not in the future),
/// <c>Amount</c> the rupees received (above 0, to the paisa), <c>PerShare</c> optional (above 0), <c>Note</c> optional plain text (at most 200 characters). The same account, holding, date and amount twice is allowed (two genuine payments) but the second asks
/// for a confirmation: <c>409 portfolio.possible_duplicate</c>, repeat with <c>Confirm</c> true.
/// </summary>
public sealed record DividendInput(Guid? AccountId, Guid? HoldingId, DateOnly? PayDate, decimal? Amount, decimal? PerShare, string? Note, bool? Confirm);

/// <summary>A dividend. <c>HoldingId</c> is null for an unallocated one; <c>Symbol</c> and <c>Isin</c> are the holding's at the time. <c>Source</c> is <c>manual</c>. A voided dividend does not count.</summary>
public sealed record DividendView(
    Guid Id,
    Guid AccountId,
    Guid? HoldingId,
    string? Symbol,
    string? Isin,
    DateOnly PayDate,
    decimal Amount,
    decimal? PerShare,
    string? Note,
    string Source,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    bool Voided,
    DateTimeOffset? VoidedAt,
    string? VoidedBy);

/// <summary>One page of dividends, newest first (pay date), voided ones included and flagged.</summary>
public sealed record DividendPage(IReadOnlyList<DividendView> Items, int Page, int PageSize, int Total);

/// <summary>
/// The suggested dividends of an account: its ledger entries of class <c>dividend</c> the owner has not accepted or dismissed, newest first. Dhan's documented ledger has no entry for an ordinary dividend, so the list is usually empty. The entries'
/// <c>Description</c> and <c>Narration</c> are Dhan's free text: untrusted, show as plain text.
/// </summary>
public sealed record DividendSuggestionPage(IReadOnlyList<LedgerEntryView> Items, int Page, int PageSize, int Total);

/// <summary>Accepting a suggested dividend: optionally the holding it is for (one of the account's; none: unallocated) and a note. The amount and the date are the ledger entry's.</summary>
public sealed record AcceptSuggestionInput(Guid? HoldingId, string? Note);

/// <summary>The decision about a suggested dividend: <c>State</c> is <c>accepted</c> (with the <c>DividendId</c> created) or <c>dismissed</c>.</summary>
public sealed record SuggestionDecisionView(Guid LedgerEntryId, string State, Guid? DividendId);
