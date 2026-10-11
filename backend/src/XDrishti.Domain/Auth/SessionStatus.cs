namespace XDrishti.Domain.Auth;

/// <summary>Why a session can or cannot be used right now.</summary>
public enum SessionStatus
{
    Active,
    Revoked,
    IdleExpired,
    AbsoluteExpired,
}
