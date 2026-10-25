using XDrishti.Domain.Common;

namespace XDrishti.Application.Paper;

/// <summary>The expected failures of the paper record (spec P10-01).</summary>
public static class PaperErrors
{
    /// <summary>The request is not usable.</summary>
    public const string InvalidCode = "paper.invalid";

    /// <summary>Nothing is recorded under this name.</summary>
    public const string NotFoundCode = "paper.not_found";

    /// <summary>The configured charge sheet is not stored.</summary>
    public const string SheetMissingCode = "paper.sheet_missing";

    /// <summary>The request cannot be used; the message says why.</summary>
    public static Error Invalid(string field, string message) => new(InvalidCode, "The request cannot be used: " + field + ": " + message, ErrorKind.Validation, Fields: [new FieldError(field, message)]);

    /// <summary>The configured sheet is not stored: nothing is captured, there is no silent gross fallback.</summary>
    public static Error SheetMissing(string sheetId) =>
        Error.Conflict(SheetMissingCode, $"Planning:Grading:CostSheetId names the charge sheet `{sheetId}`, which is not stored; nothing was captured. Store the sheet or unset the setting to capture gross.");
}
