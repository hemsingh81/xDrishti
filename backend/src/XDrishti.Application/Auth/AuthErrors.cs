using XDrishti.Domain.Common;

namespace XDrishti.Application.Auth;

/// <summary>Stable error codes of the auth module (part of the API contract).</summary>
public static class AuthErrors
{
    public const string InvalidCredentialsCode = "auth.invalid_credentials";
    public const string LockedCode = "auth.locked";
    public const string UnauthenticatedCode = "auth.unauthenticated";
    public const string SessionExpiredCode = "auth.session_expired";
    public const string OwnerMissingCode = "auth.owner_missing";
    public const string BusyCode = "auth.busy";
    public const string StepUpRequiredCode = "auth.step_up_required";
    public const string PasswordInvalidCode = "auth.password_invalid";
    public const string PasswordUnchangedCode = "auth.password_unchanged";
    public const string OwnerEmailInvalidCode = "auth.owner_email_invalid";
    public const string OwnerNameInvalidCode = "auth.owner_name_invalid";

    /// <summary>Same error for an unknown e-mail and a wrong password: no user enumeration.</summary>
    public static readonly Error InvalidCredentials = Error.Unauthorized(InvalidCredentialsCode, "Invalid e-mail or password.");

    public static readonly Error Unauthenticated = Error.Unauthorized(UnauthenticatedCode, "Authentication is required.");

    public static readonly Error SessionExpired = Error.Unauthorized(SessionExpiredCode, "The session has expired. Sign in again.");

    public static readonly Error OwnerMissing = Error.NotFound(OwnerMissingCode, "No owner account exists yet. Run the migration step first.");

    /// <summary>Too many requests changed the owner account at the same moment; nothing was counted or granted.</summary>
    public static readonly Error Busy = Error.Conflict(BusyCode, "Too many simultaneous sign-in attempts. Try again.");

    /// <summary>The session is signed in but has not re-entered the password recently enough for a sensitive action.</summary>
    public static readonly Error StepUpRequired = Error.Forbidden(StepUpRequiredCode, "Re-enter your password to continue.");

    /// <summary>The new password is shorter than 10 or longer than 1024 characters.</summary>
    public static Error PasswordInvalid() =>
        Error.Validation(PasswordInvalidCode, "The new password is not acceptable.") with
        {
            Fields = [new FieldError("newPassword", $"Use {AuthLimits.MinNewPasswordLength} to {AuthLimits.MaxPasswordLength} characters.")],
        };

    /// <summary>The new password equals the current one.</summary>
    public static Error PasswordUnchanged() =>
        Error.Validation(PasswordUnchangedCode, "The new password must be different from the current one.") with
        {
            Fields = [new FieldError("newPassword", "Choose a password different from the current one.")],
        };

    public static Error Locked(TimeSpan remaining) =>
        Error.Locked(LockedCode, "Too many failed attempts. The account is locked for a while.", (int)Math.Max(1, Math.Ceiling(remaining.TotalSeconds)));
}
