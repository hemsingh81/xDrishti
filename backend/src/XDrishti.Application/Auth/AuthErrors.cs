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

    /// <summary>Same error for an unknown e-mail and a wrong password: no user enumeration.</summary>
    public static readonly Error InvalidCredentials = Error.Unauthorized(InvalidCredentialsCode, "Invalid e-mail or password.");

    public static readonly Error Unauthenticated = Error.Unauthorized(UnauthenticatedCode, "Authentication is required.");

    public static readonly Error SessionExpired = Error.Unauthorized(SessionExpiredCode, "The session has expired. Sign in again.");

    public static readonly Error OwnerMissing = Error.NotFound(OwnerMissingCode, "No owner account exists yet. Run the migration step first.");

    /// <summary>Too many requests changed the owner account at the same moment; nothing was counted or granted.</summary>
    public static readonly Error Busy = Error.Conflict(BusyCode, "Too many simultaneous sign-in attempts. Try again.");

    public static Error Locked(TimeSpan remaining) =>
        Error.Locked(LockedCode, "Too many failed attempts. The account is locked for a while.", (int)Math.Max(1, Math.Ceiling(remaining.TotalSeconds)));
}
