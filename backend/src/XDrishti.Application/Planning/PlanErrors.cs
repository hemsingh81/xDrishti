using XDrishti.Domain.Common;

namespace XDrishti.Application.Planning;

/// <summary>The expected failures of the plan record (P7-03).</summary>
public static class PlanErrors
{
    /// <summary>The request is not usable.</summary>
    public const string InvalidCode = "plans.invalid";

    /// <summary>No plan with this id.</summary>
    public const string NotFoundCode = "plans.not_found";

    /// <summary>No ticket with this id.</summary>
    public const string ItemNotFoundCode = "plans.item_not_found";

    /// <summary>The move is not in the table or lacks a fact it requires.</summary>
    public const string MoveRefusedCode = "plans.move_refused";

    /// <summary>The state moved while the request was handled.</summary>
    public const string StaleCode = "plans.stale";

    /// <summary>The configured grading charge sheet is not stored.</summary>
    public const string GradingSheetMissingCode = "plans.grading_sheet_missing";

    /// <summary>A charge sheet is configured for grading but cannot be read: nothing is graded, there is no silent gross fallback.</summary>
    public static Error GradingSheetMissing(string sheetId) =>
        Error.Conflict(GradingSheetMissingCode, $"Planning:Grading:CostSheetId names the charge sheet `{sheetId}`, which is not stored; no ticket was graded. Store the sheet or unset the setting to grade gross.");

    /// <summary>The problems found before anything was read.</summary>
    public static Error Invalid(IReadOnlyList<FieldError> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return new Error(InvalidCode, "The request cannot be used: " + string.Join("; ", fields.Select(f => f.Field + ": " + f.Message)), ErrorKind.Validation, Fields: fields);
    }

    /// <summary>No plan has this id.</summary>
    public static Error NotFound(long id) => Error.NotFound(NotFoundCode, $"No plan {id} is recorded.");

    /// <summary>No ticket has this id.</summary>
    public static Error ItemNotFound(long id) => Error.NotFound(ItemNotFoundCode, $"No plan item {id} is recorded.");

    /// <summary>A move the table refuses.</summary>
    public static Error MoveRefused(IReadOnlyList<string> reasons) => Error.Conflict(MoveRefusedCode, string.Join(" ", reasons));

    /// <summary>Another move was written first.</summary>
    public static Error Stale() => Error.Conflict(StaleCode, "The state moved while this was handled; read it again.");
}
