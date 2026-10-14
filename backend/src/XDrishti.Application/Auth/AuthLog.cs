using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Auth;

/// <summary>Structured log events of the auth module. Only the e-mail address is logged (text typed by a client goes through <see cref="LogSanitizer"/> first) — never passwords, hashes or tokens.</summary>
internal static partial class AuthLog
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Login succeeded for {Email}")]
    public static partial void LoginSucceeded(ILogger logger, string email);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Login failed for {Email}")]
    public static partial void LoginFailed(ILogger logger, string email);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "Account {Email} locked for {Minutes} minutes after repeated failures")]
    public static partial void AccountLocked(ILogger logger, string email, int minutes);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "Login refused for {Email}: account is locked")]
    public static partial void LoginRefusedLocked(ILogger logger, string email);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Information, Message = "Logout of {Email}")]
    public static partial void LoggedOut(ILogger logger, string email);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Information, Message = "Session of {Email} ended: {Reason}")]
    public static partial void SessionExpired(ILogger logger, string email, string reason);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Information, Message = "Owner account {Email} provisioned")]
    public static partial void OwnerProvisioned(ILogger logger, string email);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Information, Message = "Owner account already exists; nothing to provision")]
    public static partial void OwnerAlreadyExists(ILogger logger);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Warning, Message = "Owner password reset for {Email}; {Count} session(s) revoked")]
    public static partial void OwnerPasswordReset(ILogger logger, string email, int count);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Information, Message = "Password hash of {Email} upgraded to current parameters")]
    public static partial void PasswordHashUpgraded(ILogger logger, string email);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Warning, Message = "Sign-in gave up after {Attempts} concurrent-update conflicts on the owner account")]
    public static partial void ConcurrencyGaveUp(ILogger logger, int attempts);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Information, Message = "Step-up succeeded for {Email}; sensitive actions allowed for {Minutes} minutes")]
    public static partial void StepUpSucceeded(ILogger logger, string email, int minutes);

    [LoggerMessage(EventId = 1013, Level = LogLevel.Warning, Message = "Step-up failed for {Email}")]
    public static partial void StepUpFailed(ILogger logger, string email);

    [LoggerMessage(EventId = 1014, Level = LogLevel.Warning, Message = "Step-up refused for {Email}: account is locked")]
    public static partial void StepUpRefusedLocked(ILogger logger, string email);

    [LoggerMessage(EventId = 1015, Level = LogLevel.Information, Message = "Password changed for {Email}; {Count} other session(s) signed out")]
    public static partial void PasswordChanged(ILogger logger, string email, int count);

    [LoggerMessage(EventId = 1016, Level = LogLevel.Warning, Message = "Password change failed for {Email}: wrong current password")]
    public static partial void PasswordChangeFailed(ILogger logger, string email);

    [LoggerMessage(EventId = 1017, Level = LogLevel.Warning, Message = "Password change refused for {Email}: account is locked")]
    public static partial void PasswordChangeRefusedLocked(ILogger logger, string email);

    [LoggerMessage(EventId = 1018, Level = LogLevel.Information, Message = "Profile of {Email} updated ({Fields})")]
    public static partial void ProfileUpdated(ILogger logger, string email, string fields);
}
