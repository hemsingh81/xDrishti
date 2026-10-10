using XDrishti.Domain.Common;

namespace XDrishti.Application.Auth;

/// <summary>Shared input rules of the owner provisioning and reset use cases.</summary>
internal static class OwnerCredentials
{
    public static Error? Validate(string? email, string? displayName, string? password)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@', StringComparison.Ordinal) || email.Trim().Length > AuthLimits.MaxEmailLength)
        {
            return Error.Validation("auth.owner_email_invalid", "A valid owner e-mail address is required.");
        }

        if (string.IsNullOrWhiteSpace(displayName) || displayName.Trim().Length > AuthLimits.MaxDisplayNameLength)
        {
            return Error.Validation("auth.owner_name_invalid", "An owner display name is required.");
        }

        if (string.IsNullOrEmpty(password) || password.Length > AuthLimits.MaxPasswordLength)
        {
            return Error.Validation("auth.owner_password_invalid", "An owner password is required.");
        }

        return null;
    }
}
