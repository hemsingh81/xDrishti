using XDrishti.Domain.Common;

namespace XDrishti.Domain.Auth;

/// <summary>
/// A server-side, revocable login session. Only a hash of the session token is kept (the CSRF token is derived from the session token, so it is not stored).
/// Ends when idle for <see cref="IdleTimeout"/> (sliding) or after <see cref="AbsoluteLifetime"/>, whichever comes first.
/// </summary>
public sealed class AuthSession : Entity<Guid>
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromHours(12);

    /// <summary>The last-seen time is written at most this often, so reads do not cause a write per request.</summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromSeconds(60);

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private AuthSession(Guid id, Guid ownerId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt, string? ip, string? userAgent)
        : base(id)
    {
        OwnerId = ownerId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        LastSeenAt = createdAt;
        ExpiresAt = expiresAt;
        Ip = ip;
        UserAgent = userAgent;
    }

    public Guid OwnerId { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastSeenAt { get; private set; }

    /// <summary>Absolute end of the session, regardless of activity.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? Ip { get; private set; }

    public string? UserAgent { get; private set; }

    public static AuthSession Start(Guid ownerId, string tokenHash, DateTimeOffset now, string? ip, string? userAgent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        return new AuthSession(Guid.CreateVersion7(now), ownerId, tokenHash, now, now + AbsoluteLifetime, ip, userAgent);
    }

    public SessionStatus Evaluate(DateTimeOffset now)
    {
        if (RevokedAt is not null)
        {
            return SessionStatus.Revoked;
        }

        if (now >= ExpiresAt)
        {
            return SessionStatus.AbsoluteExpired;
        }

        return now - LastSeenAt >= IdleTimeout ? SessionStatus.IdleExpired : SessionStatus.Active;
    }

    /// <summary>Slides the idle window. Returns true when the change must be saved (throttled to <see cref="TouchInterval"/>).</summary>
    public bool TryTouch(DateTimeOffset now)
    {
        if (now - LastSeenAt < TouchInterval)
        {
            return false;
        }

        LastSeenAt = now;
        return true;
    }

    /// <summary>Ends the session (logout, expiry, password reset). Idempotent.</summary>
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;
}
