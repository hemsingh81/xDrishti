using XDrishti.Domain.Common;

namespace XDrishti.Application.Abstractions.Research;

/// <summary>The expected failures of the strategy store (spec P3-02d, D4); P5-01 maps the <c>lab.*</c> codes to HTTP.</summary>
public static class StrategyStoreErrors
{
    /// <summary>The caller expected another version number than the next one.</summary>
    public const string VersionExistsCode = "lab.version_exists";

    /// <summary>A built-in family is changed by a release only.</summary>
    public const string BuiltInReadOnlyCode = "lab.builtin_readonly";

    /// <summary>Another family has an id that differs only in case.</summary>
    public const string IdClashCode = "lab.id_clash";

    /// <summary>The hash is not the SHA-256 of the canonical text.</summary>
    public const string IntegrityFailureCode = "strategy_store.integrity_failure";

    /// <summary>A value is not storable (id, style, hash, size, note, parent).</summary>
    public const string InvalidCode = "strategy_store.invalid";

    /// <summary>The expected version is not the next one.</summary>
    public static Error VersionExists(string id, int expected, int next) =>
        Error.Conflict(VersionExistsCode, $"The strategy `{id}` would get version {next}, not {expected}; read it again.");

    /// <summary>A user append to a built-in family.</summary>
    public static Error BuiltInReadOnly(string id) =>
        Error.Forbidden(BuiltInReadOnlyCode, $"`{id}` is a built-in strategy; clone it under another id to change it.");

    /// <summary>An id that looks like another.</summary>
    public static Error IdClash(string id, string existing) =>
        Error.Conflict(IdClashCode, $"`{id}` differs from the strategy `{existing}` only in case; use another id.");

    /// <summary>The hash does not match the canonical text.</summary>
    public static Error IntegrityFailure() =>
        new(IntegrityFailureCode, "The hash is not the SHA-256 of the canonical text. Nothing was stored.", ErrorKind.Validation);

    /// <summary>A value that cannot be stored.</summary>
    public static Error Invalid(string message) => Error.Validation(InvalidCode, message);
}
