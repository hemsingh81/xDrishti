using XDrishti.Domain.Common;

namespace XDrishti.Application.Abstractions.Research;

/// <summary>The expected failures of the result store (spec P3-10, D8).</summary>
public static class StoreErrors
{
    /// <summary>The run, its record text or its trades are not valid to store; a field error each.</summary>
    public const string RunInvalidCode = "store.run_invalid";

    /// <summary>The same run key is stored with another record: a corruption signal.</summary>
    public const string RunConflictCode = "store.run_conflict";

    /// <summary>The write failed or its count check did not pass; nothing was stored.</summary>
    public const string WriteFailedCode = "store.write_failed";

    /// <summary>A page size or a cursor is not valid.</summary>
    public const string PageInvalidCode = "store.page_invalid";

    /// <summary>A stored row holds a value the code does not know (an unknown enum name, another schema).</summary>
    public const string RowUnreadableCode = "store.row_unreadable";

    /// <summary>The problems found before the database was touched.</summary>
    public static Error RunInvalid(IReadOnlyList<FieldError> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return new Error(RunInvalidCode, "The run cannot be stored: " + string.Join("; ", fields.Select(f => f.Field + ": " + f.Message)), ErrorKind.Validation, Fields: fields);
    }

    /// <summary>The same key with other figures. Names the figures that differ, never a value.</summary>
    public static Error RunConflict(string runKey, IReadOnlyList<string> differing)
    {
        ArgumentNullException.ThrowIfNull(runKey);
        ArgumentNullException.ThrowIfNull(differing);
        return new Error(
            RunConflictCode,
            $"Run {runKey[..Math.Min(12, runKey.Length)]} is already stored with a different {string.Join(", ", differing)}: the same inputs gave another result (a corruption signal). Nothing was written.",
            ErrorKind.Conflict,
            Fields: [.. differing.Select(d => new FieldError(d, "differs from the stored run"))]);
    }

    /// <summary>The write was rolled back.</summary>
    public static Error WriteFailed(string message) => new(WriteFailedCode, message);

    /// <summary>A bad page size or cursor.</summary>
    public static Error PageInvalid(string message) => Error.Validation(PageInvalidCode, message);

    /// <summary>A damaged row.</summary>
    public static Error RowUnreadable(string message) => new(RowUnreadableCode, message);
}
