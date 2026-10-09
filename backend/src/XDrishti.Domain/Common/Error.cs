namespace XDrishti.Domain.Common;

/// <summary>A failure that is part of normal business flow (validation, not found, conflict). Not an exception.</summary>
public sealed record Error(string Code, string Message, ErrorKind Kind = ErrorKind.Failure)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);

    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);

    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);
}

public enum ErrorKind
{
    Failure,
    Validation,
    NotFound,
    Conflict,
}
