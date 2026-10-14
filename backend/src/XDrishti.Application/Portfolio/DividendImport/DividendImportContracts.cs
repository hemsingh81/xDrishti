namespace XDrishti.Application.Portfolio.DividendImport;

// Request and response shapes of the dividend file import API (ADR 0028, P2-15). Dates are India dates, amounts rupees.

/// <summary>
/// What was in the file. <c>Rows</c> are the data rows (empty lines do not count), <c>Parsed</c> the valid rows (<c>ParsedAmount</c> their rupees), <c>Repeated</c> the valid rows that repeat an earlier row of the file identically (counted once; listed in
/// <c>IdenticalRepeats</c>, at most 200), <c>Distinct</c> the rows left (<c>Parsed - Repeated</c>). Of the distinct rows: <c>AlreadyImported</c> are stored already, <c>PossibleDuplicates</c> equal a dividend you typed in, and <c>ToAdd</c> would be stored,
/// among them <c>NeedsMatch</c> (no holding or instrument found: stored unallocated with the file's name) and <c>Ambiguous</c> (more than one holding fits: stored unallocated unless you choose). <c>Bad</c> lists the first 200 bad rows (line, reason);
/// <c>BadTotal</c> counts all of them.
/// </summary>
public sealed record DividendFileSummary(
    string FileName,
    int Rows,
    int Parsed,
    decimal ParsedAmount,
    int Repeated,
    int Distinct,
    IReadOnlyList<DividendRepeat> IdenticalRepeats,
    IReadOnlyList<DividendBadRow> Bad,
    int BadTotal,
    int AlreadyImported,
    int PossibleDuplicates,
    int ToAdd,
    int NeedsMatch,
    int Ambiguous);

/// <summary>A holding that fits an ambiguous scrip name.</summary>
public sealed record DividendCandidate(Guid HoldingId, string Symbol, string Name);

/// <summary>
/// What a row's scrip name means. <c>Kind</c> is the rule that decided: <c>alias</c> (remembered), <c>isin</c>, <c>symbol</c> (the file's columns), <c>name</c> (equal names), <c>contains</c> (part of a name), <c>master</c> (a share of the instrument list that
/// is not held), <c>choice</c> (your pick in an import) or <c>none</c>. With a <c>HoldingId</c> the dividend belongs to that holding; with only an <c>InstrumentId</c> it keeps the instrument, ISIN and symbol but no holding; with neither it is
/// unallocated. <c>Candidates</c> (two or more) are the holdings an ambiguous name fits: nothing is guessed. <c>Symbol</c>, <c>Isin</c> and <c>Name</c> are those of the holding or instrument (plain text).
/// </summary>
public sealed record DividendMatchView(string Kind, Guid? HoldingId, Guid? InstrumentId, string? Symbol, string? Isin, string? Name, IReadOnlyList<DividendCandidate> Candidates);

/// <summary>
/// A distinct row of the file. <c>Name</c> is the scrip name as the file wrote it (plain text, untrusted); <c>NameKey</c> is the name reduced to what the server compares names by (case, punctuation and company suffixes ignored,
/// the ampersand read as and): two spellings of one company have one <c>NameKey</c>, so group the rows that need a choice by it and key the choices by it. <c>Status</c>: <c>new</c>, <c>imported</c> (stored already), <c>possible_duplicate</c>, <c>needs_match</c>, <c>ambiguous</c>.
/// <c>Warnings</c>: <c>amount_mismatch</c> (per share × quantity differs from the amount paid by more than ₹0.01; the amount paid is used), <c>before_first_lot</c> (the date is before the account's first lot, so the net XIRR cannot count it),
/// <c>partial_name_match</c> (matched by part of the name: check it).
/// </summary>
public sealed record DividendPreviewItem(
    int Line,
    DateOnly PayDate,
    string Name,
    string NameKey,
    decimal? PerShare,
    long? Quantity,
    decimal Amount,
    string Status,
    DividendMatchView Match,
    IReadOnlyList<string> Warnings);

/// <summary>Rows and rupees of one year.</summary>
public sealed record DividendYearTotal(int Year, int Count, decimal Amount);

/// <summary>What an import would add: the number of dividends and rupees (the rows that are to be added — not stored yet, not possible duplicates) and the same by pay year.</summary>
public sealed record DividendTotals(int Count, decimal Amount, IReadOnlyList<DividendYearTotal> ByYear);

/// <summary>The dividends to be added that are dated before the account's first lot (<c>FirstLotDate</c>; null: the account has no lot): the net XIRR cannot count an inflow before the first outflow — read the earlier trade history.</summary>
public sealed record DividendBeforeFirstLot(int Count, decimal Amount, DateOnly? FirstLotDate);

/// <summary>
/// The preview of a dividend file import — nothing is stored. <c>Format</c> is <c>dividends</c>. <c>Items</c> are the distinct rows in file order. <c>Digest</c> fingerprints what is shown; an import must send it back, and answers
/// <c>409 portfolio.preview_stale</c> when holdings, aliases or dividends changed since.
/// </summary>
public sealed record DividendPreviewReport(string Format, DividendFileSummary File, IReadOnlyList<DividendPreviewItem> Items, DividendTotals Totals, DividendBeforeFirstLot BeforeFirstLot, string Digest);

/// <summary>
/// What the owner decided for a scrip name of the file: exactly one of <c>HoldingId</c> (one of the account's holdings), <c>InstrumentId</c> (an NSE share or ETF of the instrument list) and <c>None</c> (keep the dividends unallocated). It applies to every
/// row with that name. <c>Name</c> is the scrip name as the file wrote it or the item's <c>NameKey</c> (best: send the <c>NameKey</c>); either way it is reduced to the key names are compared by (case, punctuation and company suffixes
/// ignored), so two spellings of one company are one scrip. Several choices for one scrip are fine when they agree (one is made of them) and a 400 when they disagree. With <c>Remember</c> the choice is saved as an alias, so later files with that name need no question.
/// </summary>
public sealed record DividendChoice(string Name, Guid? HoldingId = null, Guid? InstrumentId = null, bool None = false, bool Remember = false);

/// <summary>One import of a dividend file. <c>Status</c> is <c>imported</c> or <c>undone</c>; <c>ActiveDividends</c> counts its dividends that are not voided; <c>Amount</c> is the rupees it stored.</summary>
public sealed record DividendBatchView(
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
    int ActiveDividends,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UndoneAt);

/// <summary>One page of import batches, newest first.</summary>
public sealed record DividendBatchPage(IReadOnlyList<DividendBatchView> Items, int Page, int PageSize, int Total);

/// <summary>
/// What an import did. <c>Batch</c> is null when nothing new was stored. <c>Imported</c> dividends (<c>Amount</c> rupees) were stored: <c>Allocated</c> to a holding, <c>InstrumentOnly</c> with an instrument but no holding, <c>Unallocated</c> with the
/// file's name only. <c>AlreadyImported</c> rows were stored before, <c>PossibleDuplicatesSkipped</c> equal a dividend you typed in and were not ticked, <c>AliasesSaved</c> names were remembered.
/// </summary>
public sealed record DividendImportResult(
    DividendBatchView? Batch,
    int Imported,
    decimal Amount,
    int Allocated,
    int InstrumentOnly,
    int Unallocated,
    int AlreadyImported,
    int PossibleDuplicatesSkipped,
    int AliasesSaved);

/// <summary>What undoing a batch did: its dividends are voided (nothing is deleted). Undoing a batch again is a no-op (<c>AlreadyUndone</c>, <c>Voided</c> 0).</summary>
public sealed record DividendUndoResult(DividendBatchView Batch, bool AlreadyUndone, int Voided);
