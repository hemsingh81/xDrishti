using XDrishti.Domain.Common;

namespace XDrishti.Application.Auth;

/// <summary>Shared input rules of the owner provisioning and reset use cases.</summary>
internal static class OwnerCredentials
{
    public static Error? Validate(string? email, string? displayName, string? password)
    {
        if (ValidateProfile(email, displayName) is { } invalid)
        {
            return invalid;
        }

        if (string.IsNullOrEmpty(password) || password.Length > AuthLimits.MaxPasswordLength)
        {
            return Error.Validation("auth.owner_password_invalid", "An owner password is required.");
        }

        return null;
    }

    /// <summary>The display name and sign-in e-mail (shared by provisioning, reset and the profile update).</summary>
    public static Error? ValidateProfile(string? email, string? displayName)
    {
        if (!IsAcceptableEmail(email))
        {
            return Error.Validation(AuthErrors.OwnerEmailInvalidCode, "A valid owner e-mail address is required.")
                with
            { Fields = [new FieldError("email", $"Enter a valid e-mail address (at most {AuthLimits.MaxEmailLength} characters).")] };
        }

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > AuthLimits.MaxDisplayNameLength)
        {
            return Error.Validation(AuthErrors.OwnerNameInvalidCode, "An owner display name is required.")
                with
            { Fields = [new FieldError("displayName", $"Enter a name of 1 to {AuthLimits.MaxDisplayNameLength} characters.")] };
        }

        return null;
    }

    /// <summary>An address with an '@', within the length limit, and no control characters or whitespace inside (surrounding blanks are trimmed by the caller).</summary>
    private static bool IsAcceptableEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var trimmed = email.Trim();
        return trimmed.Length <= AuthLimits.MaxEmailLength
            && trimmed.Contains('@', StringComparison.Ordinal)
            && !trimmed.Any(c => char.IsControl(c) || char.IsWhiteSpace(c));
    }

    /// <summary>Length rules of a password the owner chooses.</summary>
    public static bool IsAcceptableNewPassword(string? password) =>
        password is { Length: >= AuthLimits.MinNewPasswordLength and <= AuthLimits.MaxPasswordLength };
}
