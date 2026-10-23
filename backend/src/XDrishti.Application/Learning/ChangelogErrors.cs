using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The expected failures of the learning changelog (P6-08).</summary>
public static class ChangelogErrors
{
    /// <summary>No row with this id.</summary>
    public const string NotFoundCode = "changelog.not_found";

    /// <summary>The lifecycle table or its gates refuse the move.</summary>
    public const string TransitionRefusedCode = "changelog.transition_refused";

    /// <summary>The cell changed since it was read.</summary>
    public const string StaleCode = "changelog.stale";

    /// <summary>The row cannot be reverted.</summary>
    public const string NotRevertibleCode = "changelog.not_revertible";

    /// <summary>The rows of the cell do not add up.</summary>
    public const string InconsistentCode = "changelog.inconsistent";

    /// <summary>No row has this id.</summary>
    public static Error NotFound(long id) => Error.NotFound(NotFoundCode, $"No changelog row {id}.");

    /// <summary>The move is illegal or its gates are not met.</summary>
    public static Error TransitionRefused(string cell, IReadOnlyList<string> reasons) =>
        Error.Conflict(TransitionRefusedCode, $"The cell {cell} cannot make this move: {string.Join(" ", reasons)}");

    /// <summary>Another row was written first.</summary>
    public static Error Stale(string subject) => Error.Conflict(StaleCode, $"{subject} changed while this was being recorded; read its state again.");

    /// <summary>A row that is not the latest transition of its cell.</summary>
    public static Error NotRevertible(long id, IReadOnlyList<string> reasons) =>
        Error.Conflict(NotRevertibleCode, $"Row {id} cannot be reverted: {string.Join(" ", reasons)}");

    /// <summary>The log of a cell has a break; nothing is appended on top of it.</summary>
    public static Error Inconsistent(string cell, IReadOnlyList<string> problems) =>
        Error.Conflict(InconsistentCode, $"The rows of {cell} do not add up: {string.Join(" ", problems)}");
}
