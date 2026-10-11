using XDrishti.Domain.Common;

namespace XDrishti.Domain.Accounts;

/// <summary>Stable error codes of the accounts module (part of the API contract).</summary>
public static class AccountErrors
{
    public const string NotFoundCode = "accounts.not_found";
    public const string InvalidCode = "accounts.invalid";
    public const string ConflictCode = "accounts.conflict";

    public static readonly Error NotFound = Error.NotFound(NotFoundCode, "The account does not exist.");

    public static readonly Error RiskProfileNotFound = Error.NotFound(NotFoundCode, "The risk profile does not exist.");

    /// <summary>Lost against a concurrent change or hit a database uniqueness rule that the earlier checks could not see.</summary>
    public static readonly Error ConcurrentChange = Error.Conflict(ConflictCode, "The change conflicts with a concurrent change or an existing account. Reload and try again.");

    /// <summary>Input values were rejected; carries every rejected field.</summary>
    public static Error Invalid(FieldErrors fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return fields.ToError(InvalidCode, "Some values are not valid.");
    }

    /// <summary>A single rejected field.</summary>
    public static Error Invalid(string field, string message)
    {
        var fields = new FieldErrors();
        fields.Add(field, message);
        return Invalid(fields);
    }

    public static Error Conflict(string message) => Error.Conflict(ConflictCode, message);
}
