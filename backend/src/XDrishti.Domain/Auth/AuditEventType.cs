namespace XDrishti.Domain.Auth;

/// <summary>What happened, for the authentication audit trail. Stored by name — never rename a value.</summary>
public enum AuditEventType
{
    LoginSucceeded,
    LoginFailed,
    AccountLocked,
    LoginRefusedLocked,
    Logout,
    SessionExpiredIdle,
    SessionExpiredAbsolute,
    OwnerProvisioned,
    OwnerPasswordReset,
    StepUpSucceeded,
    StepUpFailed,
}
