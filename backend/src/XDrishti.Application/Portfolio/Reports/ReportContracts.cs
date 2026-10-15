using XDrishti.Application.Portfolio.Trades;

namespace XDrishti.Application.Portfolio.Reports;

// Request and response shapes of the report import API (ADR 0029, P2-17): Dhan's downloaded reports, recognised by their title and header, previewed and imported with Undo. Dates are India dates, amounts rupees.
// Nothing here carries the personal lines of a report (name, client code, mobile, e-mail): the readers skip them.

/// <summary>A notice about a file. <c>Code</c>: <c>other_account</c> (the report's client code differs from that of the reports imported into the account before; the account remembers a hash of the first code, never the code), <c>charges_differ_from_footer</c>, <c>needs_match</c> (<c>Count</c> names that could not be tied to a security), <c>partial_name_match</c>, <c>negative_charges</c>, <c>value_not_reproduced</c>, <c>covered_by_dhan</c>, <c>totals_differ</c>, <c>bad_rows</c>, <c>holdings_differ</c> (a demat statement and the synced holdings differ; nothing is changed), <c>notice_only</c> (the file is recognised and not imported).</summary>
public sealed record ImportWarning(string Code, string Message, int? Count = null);

/// <summary>Why a file could not be read or imported: <c>portfolio.invalid</c> (<c>Field</c> <c>file</c>), <c>portfolio.wrong_file_kind</c> (an unknown shape; the message lists the accepted kinds), <c>portfolio.preview_stale</c>, <c>portfolio.other_account</c>, <c>portfolio.sync_running</c>, <c>portfolio.conflict</c>.</summary>
public sealed record ImportFileError(string Code, string Message, string? Field = null);

/// <summary>The counts every kind of file has. <c>Rows</c> are the data rows, <c>Valid</c> the rows that can be used, <c>ToAdd</c> what an import would store, <c>AlreadyImported</c> what is stored already, <c>Skipped</c> the valid rows left out (by reason in the kind's own block), <c>Bad</c> the rows that cannot be used. <c>From</c>/<c>To</c> are the period the report says it covers.</summary>
public sealed record ImportSummary(int Rows, int Valid, int ToAdd, int AlreadyImported, int Skipped, int Bad, DateOnly? From, DateOnly? To);

/// <summary>A security a name may mean: <c>Isin</c>, <c>Symbol</c>, <c>Name</c> and — when it is a holding of the account or a share of the instrument master — <c>HoldingId</c> or <c>InstrumentId</c>.</summary>
public sealed record BillCandidate(string? Isin, string? Symbol, string Name, Guid? HoldingId, Guid? InstrumentId);

/// <summary>
/// A scrip name of a global transaction report. <c>Name</c> is the name as written (plain text, untrusted), <c>NameKey</c> the name reduced to what the server compares names by (key the choices by it), <c>SymbolHint</c> the ticker in brackets. <c>Status</c>: <c>resolved</c>,
/// <c>ambiguous</c> (more than one security fits: <c>Candidates</c>) or <c>needs_match</c> (none: its trades are kept without an ISIN). <c>Kind</c> is the rule that decided: <c>choice</c>, <c>alias</c>, <c>symbol</c>, <c>holding</c>, <c>master</c>, <c>learned</c> (a demat or depository file taught
/// it), <c>partial</c> (a spelling that differs by a word's ending: check it), <c>none</c>.
/// </summary>
public sealed record BillNameView(
    string Name,
    string NameKey,
    string? SymbolHint,
    string Status,
    string Kind,
    string? Isin,
    string? Symbol,
    string? SecurityName,
    Guid? HoldingId,
    Guid? InstrumentId,
    int Rows,
    int Trades,
    IReadOnlyList<BillCandidate> Candidates);

/// <summary>A day, security and side whose total quantity differs between the report's bill rows and the broker's own trades (the broker's win; shown, not hidden).</summary>
public sealed record BillDifference(DateOnly Date, string Isin, string? Symbol, string Side, long BillQty, long DhanQty);

/// <summary>What the broker's own trades cover (ADR 0029): <c>Covered</c> trades of the report are left out because Dhan's trade history has the same day, ISIN and side; the days whose totals differ are listed (at most 50; <c>DifferencesTotal</c> counts all).</summary>
public sealed record BillCoverage(int Covered, IReadOnlyList<BillDifference> Differences, int DifferencesTotal);

/// <summary>The charges of the report's rows added up (<c>Total</c>, every row, whatever becomes of it) against the report's own total charges (<c>ReportTotal</c>; null when the report has none); <c>Matches</c> within ₹0.01.</summary>
public sealed record BillCharges(decimal Total, decimal? ReportTotal, bool Matches);

/// <summary>What is particular to a global transaction report: the names (every one that is a share or ETF, at most 500 listed; <c>NamesTotal</c> counts all) with what they mean, how many are unresolved, the coverage by the broker's trades and the charges check.</summary>
public sealed record BillDetails(IReadOnlyList<BillNameView> Names, int NamesTotal, int NeedsMatch, int NeedsMatchTrades, BillCoverage Coverage, BillCharges Charges);

/// <summary>A line of a depository-charges sheet. <c>Status</c>: <c>new</c> (stored by an import), <c>zero</c> (costs nothing), <c>buy_side</c> (not charged), <c>already_imported</c>, <c>already_in_ledger</c> (Dhan's ledger has a charge of that amount that day). <c>Side</c> is <c>buy</c>, <c>sell</c> or <c>other</c>.</summary>
public sealed record DpLineView(int Line, DateOnly Date, string Isin, string Name, long Quantity, string Side, string Type, decimal Charge, string Status);

/// <summary>The lines of a depository-charges sheet by what happens to them. <c>OffMarket</c> counts (among the others) the lines of type off-market.</summary>
public sealed record DpCounts(int Read, int ToStore, int Zero, int BuySide, int AlreadyImported, int AlreadyInLedger, int OffMarket);

/// <summary>Names a depository or demat file teaches: <c>Known</c> are remembered already, <c>New</c> would be remembered by an import.</summary>
public sealed record NameLearning(int Known, int New);

/// <summary>The preview of a depository-charges sheet: the lines (at most 300 listed, <c>LinesTotal</c> counts all), their counts, the rupees an import would add to the account's costs and the names it teaches.</summary>
public sealed record DpPreview(IReadOnlyList<DpLineView> Lines, int LinesTotal, DpCounts Counts, decimal AmountToStore, NameLearning Names);

/// <summary>A security whose depository quantity differs from the synced holding's.</summary>
public sealed record DematDifference(string Isin, string Name, decimal Statement, decimal Synced);

/// <summary>A security the depository holds that the account has no open holding for.</summary>
public sealed record DematHeld(string Isin, string Name, decimal Quantity);

/// <summary>An open holding of the account that the statement does not list.</summary>
public sealed record DematSynced(string Symbol, string? Isin, string Name, decimal Quantity);

/// <summary>
/// The preview of a demat holding statement: its securities, how many agree with the synced holdings, the quantity differences, the securities held at Dhan but not synced and the synced holdings missing from the statement (each list at most 200 long, the totals count all) and the names
/// it teaches. Nothing here changes a holding. <b>No notice is raised for these differences; they are shown here only.</b>
/// </summary>
public sealed record DematPreview(
    DateOnly? AsOf,
    int Securities,
    int Matching,
    IReadOnlyList<DematDifference> QuantityDifferences,
    int QuantityDifferencesTotal,
    IReadOnlyList<DematHeld> HeldNotSynced,
    int HeldNotSyncedTotal,
    IReadOnlyList<DematSynced> SyncedNotInStatement,
    int SyncedNotInStatementTotal,
    NameLearning Names);

/// <summary>
/// What a file is and what importing it would do — nothing is stored. <c>Kind</c>: <c>global_transaction_report</c>, <c>dp_charges</c>, <c>demat_holding</c>, <c>pnl_report</c>, <c>trades</c> (a tradebook with a buy/sell column), <c>dividends</c>, <c>tradebook_without_sides</c>,
/// <c>statement</c>, <c>unknown</c>. <c>Supported</c> is true when the file can be imported here. A file that belongs on another screen has <c>RouteTo</c> (<c>dividends</c>); one that cannot be imported has a <c>Message</c> saying why; a file that cannot be read has an <c>Error</c>. Of
/// the blocks only the one of the kind is set: <c>Trades</c> (the tradebook preview: global transaction report and tradebook) with <c>Bill</c> beside it for the global transaction report, <c>Costs</c> (depository charges), <c>Demat</c>. <c>Digest</c> fingerprints what is shown;
/// an import sends it back.
/// </summary>
public sealed record ImportFilePreview(
    string FileName,
    string Kind,
    bool Supported,
    string? RouteTo,
    string? Message,
    ImportFileError? Error,
    ImportSummary? Summary,
    IReadOnlyList<ImportWarning> Warnings,
    TradePreviewReport? Trades,
    BillDetails? Bill,
    DpPreview? Costs,
    DematPreview? Demat,
    string? Digest);

/// <summary>The previews of the files of one request, in the order they were sent.</summary>
public sealed record ImportPreviewResponse(IReadOnlyList<ImportFilePreview> Files);

/// <summary>
/// What the owner decided a scrip name of a global transaction report means: exactly one of <c>HoldingId</c> (a holding of the account), <c>InstrumentId</c> (an NSE share or ETF of the instrument list) and <c>Isin</c> (an ISIN typed in). <c>Name</c> is the name as written or the item's
/// <c>NameKey</c> (best). With <c>Remember</c> the choice is saved so later files need no question (a holding or instrument as an alias, an ISIN as a learned name).
/// </summary>
public sealed record BillChoice(string Name, Guid? HoldingId = null, Guid? InstrumentId = null, string? Isin = null, bool Remember = false);

/// <summary>One import of a depository-charges sheet. <c>Status</c> is <c>imported</c> or <c>undone</c>; <c>ActiveEntries</c> counts its cost entries that are not voided; <c>Amount</c> is the rupees it stored.</summary>
public sealed record CostBatchView(
    Guid Id,
    Guid AccountId,
    string AccountName,
    string FileName,
    string Sha256,
    int Parsed,
    int Imported,
    int Duplicates,
    int Bad,
    decimal Amount,
    string Status,
    int ActiveEntries,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UndoneAt);

/// <summary>One page of cost import batches, newest first.</summary>
public sealed record CostBatchPage(IReadOnlyList<CostBatchView> Items, int Page, int PageSize, int Total);

/// <summary>What an import of a depository-charges sheet did. <c>Batch</c> is null when nothing new was stored. <c>Imported</c> entries (<c>Amount</c> rupees) were stored; <c>AlreadyImported</c> and <c>AlreadyInLedger</c> lines were left out; <c>NamesLearned</c> names were remembered.</summary>
public sealed record CostImportResult(CostBatchView? Batch, int Imported, decimal Amount, int AlreadyImported, int AlreadyInLedger, int NamesLearned);

/// <summary>What undoing a cost batch did: its entries are voided (nothing is deleted). Undoing a batch again is a no-op (<c>AlreadyUndone</c>, <c>Voided</c> 0).</summary>
public sealed record CostUndoResult(CostBatchView Batch, bool AlreadyUndone, int Voided);

/// <summary>What an import of a demat holding statement did: it changed no holding. <c>NamesLearned</c> names were remembered, <c>NamesKnown</c> were remembered already.</summary>
public sealed record DematImportResult(int NamesLearned, int NamesKnown);

/// <summary>
/// What importing one file did. <c>Status</c>: <c>imported</c> (something was stored), <c>nothing_new</c> (nothing to store: the file is imported already), <c>not_supported</c> (the kind cannot be imported; <c>Message</c> says why), <c>rejected</c> (<c>Error</c> says why; a
/// <c>portfolio.preview_stale</c> error comes with the fresh <c>Preview</c> and nothing was stored). Of the results only the one of the kind is set: <c>Trades</c> (global transaction report and tradebook), <c>Costs</c>, <c>Demat</c>. <c>AliasesSaved</c> counts the choices remembered.
/// </summary>
public sealed record ImportFileResult(
    string FileName,
    string Kind,
    string Status,
    string? Message,
    ImportFileError? Error,
    ImportFilePreview? Preview,
    TradeImportResult? Trades,
    CostImportResult? Costs,
    DematImportResult? Demat,
    int AliasesSaved);

/// <summary>The results of the files of one request, in the order they were sent.</summary>
public sealed record ImportRunResponse(IReadOnlyList<ImportFileResult> Files);

/// <summary>Limits of a report import request.</summary>
public static class ReportImportLimits
{
    /// <summary>The most choices of what scrip names mean that one request carries.</summary>
    public const int MaxChoices = 2000;

    /// <summary>The most files one request carries.</summary>
    public const int MaxFiles = 20;
}
