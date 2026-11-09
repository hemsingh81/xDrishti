using XDrishti.Domain.Common;

namespace XDrishti.Application.Market.Eod;

/// <summary>The closed list of refusals of the history slice (CF-04). Codes are part of the contract.</summary>
public static class EodErrors
{
    public const string UnknownFormatCode = "history.unknown_format";

    public const string DisabledCode = "history.disabled";

    public const string NotAllowedCode = "history.not_allowed";

    public const string DateMismatchCode = "history.date_mismatch";

    public const string DuplicateRowCode = "history.duplicate_row";

    /// <summary>The header of a file is not one the parser knows: the whole file is refused and nothing is written.</summary>
    public static Error UnknownFormat(string file, string detail) => Error.Validation(UnknownFormatCode, $"The {file} file has a header this version does not know ({detail}). Nothing was written.");

    public static Error Disabled() => Error.Conflict(DisabledCode, "The NSE history fetch is off (Data:History:Nse:Enabled). Nothing was requested.");

    /// <summary>The allow-list does not contain the endpoint. The owner edits the list (ADR 0010, 0017, 0195); the app never does.</summary>
    public static Error NotAllowed(string detail) => Error.Forbidden(NotAllowedCode, $"{detail} The owner must add the endpoint to the master-data allow-list (see ADR 0195); the app never edits it.");

    public static Error DateMismatch(string file, DateOnly expected) => Error.Validation(DateMismatchCode, $"The {file} file for {expected:yyyy-MM-dd} holds rows of another date. Nothing was written.");

    /// <summary>The same (symbol, series) twice in one file: the whole file is refused (a day with two prices for a name has no honest row).</summary>
    public static Error DuplicateRow(string file, string symbol, string series) => Error.Validation(DuplicateRowCode, $"The {file} file has more than one row for {symbol} ({series}). Nothing was written.");
}
