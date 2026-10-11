using XDrishti.Domain.Common;

namespace XDrishti.Domain.Accounts;

/// <summary>
/// A browser sign-in at Dhan that was started and has not come back yet. It belongs to the session that started it, is valid for
/// <see cref="Lifetime"/> and can be used once. Dhan sends no state back, so a callback uses the session's open sign-in. Old ones are kept
/// for a while: they count Dhan's daily consents (<see cref="DailyConsentLimit"/>).
/// </summary>
public sealed class PendingConnect : Entity<Guid>
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>How many consents Dhan allows per account and day.</summary>
    public const int DailyConsentLimit = 25;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private PendingConnect(Guid id, Guid accountId, Guid sessionId, DateTimeOffset createdAt, DateTimeOffset expiresAt)
        : base(id)
    {
        AccountId = accountId;
        SessionId = sessionId;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public Guid AccountId { get; private set; }

    public Guid SessionId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When it was used (or replaced by a newer sign-in of the same session); null while open.</summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    public static PendingConnect Start(Guid accountId, Guid sessionId, DateTimeOffset now) =>
        new(Guid.CreateVersion7(now), accountId, sessionId, now, now + Lifetime);

    /// <summary>Whether a callback of <paramref name="sessionId"/> may use it now.</summary>
    public PendingConnectCheck Check(Guid sessionId, DateTimeOffset now)
    {
        if (sessionId != SessionId)
        {
            return PendingConnectCheck.OtherSession;
        }

        if (ConsumedAt is not null)
        {
            return PendingConnectCheck.Used;
        }

        return now >= ExpiresAt ? PendingConnectCheck.Expired : PendingConnectCheck.Open;
    }

    /// <summary>Marks it used; false when it was used already.</summary>
    public bool Consume(DateTimeOffset now)
    {
        if (ConsumedAt is not null)
        {
            return false;
        }

        ConsumedAt = now;
        return true;
    }
}
