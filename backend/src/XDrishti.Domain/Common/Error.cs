namespace XDrishti.Domain.Common;

/// <summary>A failure that is part of normal business flow (validation, not found, conflict). Not an exception.</summary>
public sealed record Error(string Code, string Message, ErrorKind Kind = ErrorKind.Failure, int? RetryAfterSeconds = null)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);

    public static Error Locked(string code, string message, int retryAfterSeconds) => new(code, message, ErrorKind.Locked, retryAfterSeconds);
}

public enum ErrorKind
{
    Failure,
    Validation,
    NotFound,
    Conflict,

    /// <summary>Missing or wrong credentials / no valid session.</summary>
    Unauthorized,

    /// <summary>Temporarily refused; retry after <see cref="Error.RetryAfterSeconds"/>.</summary>
    Locked,
}
