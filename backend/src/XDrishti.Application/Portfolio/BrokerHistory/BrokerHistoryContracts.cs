using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.BrokerHistory;

// Request and response shapes of the broker history API (ADR 0025, P2-13). Dates are India dates, amounts rupees.

/// <summary>Optional body of "Sync now" for the broker history: the first date to read, instead of the watermark (or, for an account never read, the first import date).</summary>
public sealed record BrokerHistoryInput(DateOnly? From);

/// <summary>
/// A run that reads an account's trades or its ledger. <c>Kind</c> <c>trades</c> or <c>ledger</c>; <c>Trigger</c> <c>manual</c>, <c>scheduled</c> or <c>first</c> (the scheduled backfill of an account never read); <c>Status</c> <c>running</c>,
/// <c>succeeded</c>, <c>failed</c> or <c>skipped</c>; <c>From</c> and <c>To</c> the dates it reads. <c>Counts</c> says what it did (windows done of total, rows read, new, already stored, unusable, skipped by reason; after a trade run the holdings
/// rebuilt, unchanged and needing review). <c>Error</c> is a short safe text.
/// </summary>
public sealed record BrokerSyncRunView(
    Guid Id,
    Guid AccountId,
    string Kind,
    string Trigger,
    string Status,
    DateOnly From,
    DateOnly To,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    BrokerSyncCounts Counts,
    string? Error);

/// <summary>The answer of "Sync now": the trades run and, when it succeeded and has been carried on to it, the ledger run. Started over the API, the answer holds the trades run as it begins (<c>running</c>); the status says how far both are.</summary>
public sealed record BrokerHistoryRunView(BrokerSyncRunView Trades, BrokerSyncRunView? Ledger);

/// <summary>How far one kind is read: the <c>Watermark</c> (last date read; null when nothing was read yet), the latest run and its windows done of total.</summary>
public sealed record BrokerKindStatus(DateOnly? Watermark, BrokerSyncRunView? LastRun, int WindowsDone, int WindowsTotal);

/// <summary>
/// The status of the broker history sync of an account: whether a run is running, both kinds, the next scheduled run (null when the service is paused or there is none) and three figures that come from <b>the latest <i>succeeded</i> trades run</b> and
/// from no other (nothing is carried forward from older runs, and a run that was skipped, failed or is still running does not change them): <c>HoldingsRebuilt</c> (rebuilt automatically by that run), <c>HoldingsNeedingReview</c> (a hand-dated lot; the owner
/// uses the holding's rebuild preview) and <c>NeedsReviewSymbols</c>, the trading symbols of those holdings (at most 50, in the order the run listed them: alphabetical; <c>HoldingsNeedingReview</c> says how many there are in all). A quiet run that skipped the rebuild
/// because nothing changed reports 0 rebuilt (holdings that need review are never skipped: every run looks at them again).
/// </summary>
public sealed record BrokerHistoryStatusView(
    bool Running,
    BrokerKindStatus Trades,
    BrokerKindStatus Ledger,
    DateTimeOffset? NextRunAt,
    int HoldingsRebuilt,
    int HoldingsNeedingReview,
    IReadOnlyList<string> NeedsReviewSymbols);

/// <summary>A ledger entry. <c>Class</c> is <c>funds_in</c>, <c>funds_out</c>, <c>dividend</c>, <c>charges</c> or <c>other</c>; <c>Amount</c> is signed (credit minus debit). <c>Description</c> and <c>Narration</c> are Dhan's free text (untrusted: show as plain text).</summary>
public sealed record LedgerEntryView(
    Guid Id,
    DateOnly Date,
    string VoucherNo,
    string Description,
    string Narration,
    string? Exchange,
    decimal Amount,
    string Class,
    decimal? RunningBalance);

/// <summary>The entries of one class in the period and their signed total.</summary>
public sealed record LedgerClassTotal(string Class, int Count, decimal Amount);

/// <summary>
/// One page of the ledger, newest first, and the totals of every class for the period (<c>from</c> to <c>to</c>, whatever class is filtered). <c>CoverageFrom</c> is the earliest date of any ledger entry stored for the account, whatever the filters
/// (null: none stored): the ledger's costs are known from this date on, which reading from an earlier date can move back (ADR 0026).
/// </summary>
public sealed record LedgerPage(IReadOnlyList<LedgerEntryView> Items, int Page, int PageSize, int Total, IReadOnlyList<LedgerClassTotal> Totals, DateOnly? CoverageFrom = null);
