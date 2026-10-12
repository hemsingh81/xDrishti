using XDrishti.Domain.Market;
using XDrishti.Domain.Market.Actions;

namespace XDrishti.Application.Market.Actions;

/// <summary>
/// A split or bonus issue of an instrument (ADR 0017 §5). <c>kind</c>: split · bonus. <c>numerator:denominator</c> (<c>ratio</c>): for a split old shares : new shares (1:5 is one share into five), for a bonus
/// bonus shares : shares held (1:1 is one bonus share for each held). <c>factor</c> is the price multiplier for the bars before <c>exDate</c> (a 1:5 split 0.2, a 1:1 bonus 0.5). <c>status</c>: suggested
/// (found by the detection, changes nothing yet) · applied (adjusted reads use it) · ignored (declined, kept so it is not suggested again). <c>source</c>: manual · detected · csv.
/// <c>decidedAt</c> and <c>decidedBy</c> are set once the owner decided (not while suggested). <c>csvAdjustedThrough</c>: the date the instrument's loaded CSV files reach (their last bar, in India time; null when
/// none is loaded). Those files are already adjusted at source, so an action with an ex-date on or before it would adjust their prices twice.
/// </summary>
public sealed record CorporateActionView(
    Guid Id,
    Guid InstrumentId,
    string Symbol,
    string Name,
    DateOnly ExDate,
    string Kind,
    int Numerator,
    int Denominator,
    string Ratio,
    double Factor,
    string Status,
    string Source,
    string? Note,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt,
    string? DecidedBy,
    DateOnly? CsvAdjustedThrough = null)
{
    public static CorporateActionView From(CorporateAction action, string symbol, string name)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new CorporateActionView(
            action.Id,
            action.InstrumentId,
            symbol,
            name,
            action.ExDate,
            action.Kind.ToCode(),
            action.Numerator,
            action.Denominator,
            action.Ratio,
            (double)action.Factor,
            action.Status.ToCode(),
            action.Source.ToCode(),
            action.Note,
            IndiaTime.ToIst(action.CreatedAt),
            action.DecidedAt is { } decided ? IndiaTime.ToIst(decided) : null,
            action.DecidedBy);
    }
}

/// <summary>A page of corporate actions, newest ex-date first. <c>pending</c> is the number of suggestions waiting for a decision over all instruments, whatever the filter.</summary>
public sealed record CorporateActionPage(IReadOnlyList<CorporateActionView> Items, int Page, int PageSize, int Total, int Pending);

/// <summary>The list as received (query string values, unchecked): <c>status</c> suggested|applied|ignored, <c>instrumentId</c>, <c>page</c> from 1, <c>pageSize</c> up to 200.</summary>
public sealed record CorporateActionRequest(string? Status, Guid? InstrumentId, int? Page, int? PageSize);

/// <summary>A checked list search.</summary>
public sealed record CorporateActionSearch(CorporateActionStatus? Status, Guid? InstrumentId, int Page, int PageSize)
{
    public const int MaxPageSize = 200;

    public const int DefaultPageSize = 50;

    public const int MaxPage = 100_000;
}

/// <summary>
/// A corporate action to add by hand (it is applied at once). <c>exDate</c> <c>yyyy-MM-dd</c> (2000-01-01 to a year ahead); <c>kind</c> split|bonus; <c>numerator</c> and <c>denominator</c> whole numbers 1 to 100000
/// (a split 1:1 changes nothing and is refused); <c>note</c> at most 200 characters. <c>acknowledgeCsvAdjusted</c> true says the owner knows the loaded CSV files are adjusted up to <c>csvAdjustedThrough</c>
/// and wants an ex-date on or before it anyway (otherwise 409 <c>data.csv_already_adjusted</c>).
/// </summary>
public sealed record AddCorporateActionRequest(Guid? InstrumentId, string? ExDate, string? Kind, int? Numerator, int? Denominator, string? Note, bool? AcknowledgeCsvAdjusted = null);

/// <summary>
/// Changes an action and makes it applied (confirming a suggestion, bringing back an ignored one). Every field is optional and keeps its current value when left out (an empty body confirms as it is);
/// <c>note</c> left out keeps the note, an empty text clears it. <c>acknowledgeCsvAdjusted</c> as when adding: needed when the result is applied with an ex-date on or before <c>csvAdjustedThrough</c>.
/// </summary>
public sealed record UpdateCorporateActionRequest(string? ExDate, string? Kind, int? Numerator, int? Denominator, string? Note, bool? AcknowledgeCsvAdjusted = null);

/// <summary>What a detection run did: <c>instruments</c> looked at, <c>candidates</c> found, <c>suggested</c> new suggestions stored (the others were known already, ignored ones included) and <c>pending</c> suggestions now waiting.</summary>
public sealed record DetectionResult(int Instruments, int Candidates, int Suggested, int Pending);

/// <summary>A line of an imported text that could not be used, with the reason (no free text from the file is repeated).</summary>
public sealed record ImportLineError(int Line, string Message);

/// <summary>
/// A valid line whose ex-date is on or before the date the instrument's loaded CSV files reach (<c>csvAdjustedThrough</c>): importing it would adjust those already adjusted prices twice.
/// <c>symbol</c> is the canonical symbol (<c>NSE:ABB</c>).
/// </summary>
public sealed record ImportLineWarning(int Line, string Symbol, DateOnly ExDate, DateOnly CsvAdjustedThrough, string Message);

/// <summary>
/// The outcome of a CSV import (or its preview). <c>lines</c>: data lines read; <c>importable</c>: new valid actions; <c>imported</c>: written (0 for a preview); <c>duplicates</c>: lines that repeat an action
/// that exists (any status) or an earlier line; <c>invalid</c>: lines with a problem (the first 50 in <c>errors</c>, among them lines that clash with an applied action of the other kind on the same day);
/// <c>unknownSymbols</c>: symbols that match no listed instrument (never guessed). <c>adjustsCsvTwice</c>: how many of the importable lines have an ex-date on or before the date their instrument's loaded
/// CSV files reach, listed in <c>warnings</c> (the first 50); an import (not a preview) with such lines needs <c>acknowledgeCsvAdjusted=true</c>, else 409 <c>data.csv_already_adjusted</c>.
/// </summary>
public sealed record ImportCorporateActionsResult(
    bool Preview,
    int Lines,
    int Importable,
    int Imported,
    int Duplicates,
    int Invalid,
    IReadOnlyList<string> UnknownSymbols,
    IReadOnlyList<ImportLineError> Errors,
    int? AdjustsCsvTwice = null,
    IReadOnlyList<ImportLineWarning>? Warnings = null);

/// <summary>One applied action in the <c>adjustment</c> block of a bars read: the ex-date, kind, ratio and the factor of the action itself.</summary>
public sealed record AdjustmentActionView(DateOnly ExDate, string Kind, string Ratio, double Factor);

/// <summary>
/// What an adjusted read applied: <c>applied</c> is the number of applied actions that changed the bars returned (those with an ex-date after the oldest bar), <c>factor</c> the cumulative factor of the oldest bar
/// returned (the strongest; newer bars have a factor closer to 1) and <c>actions</c> those actions, oldest ex-date first.
/// </summary>
public sealed record AdjustmentView(int Applied, double Factor, IReadOnlyList<AdjustmentActionView> Actions);
