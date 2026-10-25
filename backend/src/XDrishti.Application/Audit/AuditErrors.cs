using XDrishti.Domain.Common;

namespace XDrishti.Application.Audit;

/// <summary>The expected failures of the audit screen (spec P8-22).</summary>
public static class AuditErrors
{
    public const string InvalidCode = "audit.invalid";

    public const string NotFoundCode = "audit.not_found";

    public static Error Invalid(IReadOnlyList<FieldError> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return new Error(InvalidCode, "The request cannot be used: " + string.Join("; ", fields.Select(f => f.Field + ": " + f.Message)), ErrorKind.Validation, Fields: fields);
    }

    public static Error NotFound() => Error.NotFound(NotFoundCode, "No such audit entry.");
}
