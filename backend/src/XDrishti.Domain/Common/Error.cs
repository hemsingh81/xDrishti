namespace XDrishti.Domain.Common;

/// <summary>A failure that is part of normal business flow (validation, not found, conflict). Not an exception.</summary>
/// <remarks>
/// <c>Data</c> carries named counts that the API passes on as extension members of the problem response (for example how many holdings a sync would close). <c>Payload</c> carries named objects (a report) that the API
/// passes on the same way; the API must be able to serialise their types. <c>Cause</c> is the structured kind of a broker or sync failure (ADR 0030): decisions such as "retry quietly" read it, never the message text; the API does not show it.
/// </remarks>
public sealed record Error(
    string Code,
    string Message,
    ErrorKind Kind = ErrorKind.Failure,
    int? RetryAfterSeconds = null,
    IReadOnlyList<FieldError>? Fields = null,
    IReadOnlyDictionary<string, int>? Data = null,
    IReadOnlyDictionary<string, object>? Payload = null,
    FailureKind? Cause = null)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);

    public static Error Locked(string code, string message, int retryAfterSeconds) => new(code, message, ErrorKind.Locked, retryAfterSeconds);

    public static Error Upstream(string code, string message) => new(code, message, ErrorKind.Upstream);

    public static Error RateLimited(string code, string message, int retryAfterSeconds) => new(code, message, ErrorKind.RateLimited, retryAfterSeconds);
}

public enum ErrorKind
{
    Failure,
    Validation,
    NotFound,
    Conflict,

    /// <summary>Missing or wrong credentials / no valid session.</summary>
    Unauthorized,

    /// <summary>Signed in, but not allowed to do this right now (for example the password must be re-entered first).</summary>
    Forbidden,

    /// <summary>Temporarily refused; retry after <see cref="Error.RetryAfterSeconds"/>.</summary>
    Locked,

    /// <summary>A remote service this request depends on (a broker) failed, could not be reached or refused the call.</summary>
    Upstream,

    /// <summary>A usage limit is reached (for example a broker's daily quota); retry after <see cref="Error.RetryAfterSeconds"/>.</summary>
    RateLimited,
}
