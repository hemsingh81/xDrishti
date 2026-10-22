using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Lab;

/// <summary>The expected failures of the Lab API (spec P5-01, ADR 0057). The store's own codes (<c>lab.version_exists</c>, <c>lab.builtin_readonly</c>, <c>lab.id_clash</c>) pass through.</summary>
public static class LabErrors
{
    /// <summary>The text has at least one error; the located problems are in the error payload under <see cref="ProblemsKey"/>.</summary>
    public const string InvalidStrategyCode = "lab.invalid_strategy";

    /// <summary>A stored text no longer reads under the rules of today, so it cannot be cloned.</summary>
    public const string StoredUnreadableCode = "lab.stored_unreadable";

    /// <summary>No such family or version.</summary>
    public const string NotFoundCode = "lab.not_found";

    /// <summary>The text is larger than <see cref="MaxTextBytes"/>; refused before it is read.</summary>
    public const string TextTooLargeCode = "lab.text_too_large";

    /// <summary>The name of the payload entry that carries the problems.</summary>
    public const string ProblemsKey = "problems";

    /// <summary>The most bytes (UTF-8) of a strategy text the Lab accepts (owner answer to P5-01 question 5); the file reader's own limit is higher.</summary>
    public const int MaxTextBytes = 64 * 1024;

    /// <summary>The text is refused: <see cref="ErrorKind.Validation"/>, the API answers 422.</summary>
    public static Error InvalidStrategy(IReadOnlyList<StrategyIssue> problems) =>
        new(InvalidStrategyCode, "The strategy has problems; nothing was stored.", ErrorKind.Validation, Payload: new Dictionary<string, object> { [ProblemsKey] = problems });

    /// <summary>A family or a version that is not stored.</summary>
    public static Error NotFound(string id, int? version = null) =>
        Error.NotFound(NotFoundCode, version is null ? $"No strategy `{id}` is stored." : $"The strategy `{id}` has no version {version}.");

    /// <summary>The issue for a text over the limit.</summary>
    public static StrategyIssue TooLarge() =>
        new(string.Empty, TextTooLargeCode, $"The strategy text is larger than {MaxTextBytes} bytes.", "error", null, null, null, null, null, null, null);

    /// <summary>A built-in is read-only: a conflict (409), whatever kind the store gave it.</summary>
    public static Error AsConflict(Error storeError) =>
        storeError.Code == StrategyStoreErrors.BuiltInReadOnlyCode ? Error.Conflict(storeError.Code, storeError.Message) : storeError;
}
