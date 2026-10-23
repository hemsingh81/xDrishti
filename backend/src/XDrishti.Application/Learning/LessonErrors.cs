using XDrishti.Domain.Common;

namespace XDrishti.Application.Learning;

/// <summary>The expected failures of the lessons (P6-16).</summary>
public static class LessonErrors
{
    /// <summary>No lesson with this id.</summary>
    public const string NotFoundCode = "lessons.not_found";

    /// <summary>The move is not legal or lacks a fact the table requires.</summary>
    public const string MoveRefusedCode = "lessons.move_refused";

    /// <summary>The lesson moved while the request was handled.</summary>
    public const string StaleCode = "lessons.stale";

    /// <summary>The fix to link does not exist.</summary>
    public const string FixNotFoundCode = "lessons.fix_not_found";

    /// <summary>No lesson has this id.</summary>
    public static Error NotFound(long id) => Error.NotFound(NotFoundCode, $"No lesson {id} is recorded.");

    /// <summary>A move the table refuses.</summary>
    public static Error MoveRefused(IReadOnlyList<string> reasons) => Error.Conflict(MoveRefusedCode, string.Join(" ", reasons));

    /// <summary>Another move was written first.</summary>
    public static Error Stale() => Error.Conflict(StaleCode, "The lesson moved while this was handled; read it again.");

    /// <summary>The named hypothesis or experiment is not recorded.</summary>
    public static Error FixNotFound(string kind, long id) => Error.NotFound(FixNotFoundCode, $"No {kind} {id} is recorded.");
}
