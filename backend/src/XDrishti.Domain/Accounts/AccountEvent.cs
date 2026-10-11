using XDrishti.Domain.Common;

namespace XDrishti.Domain.Accounts;

/// <summary>
/// One entry of the accounts audit trail (insert-only). The detail is a short human-readable summary and never contains secrets or
/// the client id; callers build it from field names and non-secret values only.
/// </summary>
public sealed class AccountEvent : Entity<Guid>
{
    public const int MaxDetailLength = 512;

    public const int MaxActorLength = 256;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private AccountEvent(Guid id, DateTimeOffset at, Guid? accountId, AccountEventType @event, string? detail, string actor)
        : base(id)
    {
        At = at;
        AccountId = accountId;
        Event = @event;
        Detail = detail;
        Actor = actor;
    }

    public DateTimeOffset At { get; private set; }

    /// <summary>The account concerned; null for events about shared things (risk profiles, buckets).</summary>
    public Guid? AccountId { get; private set; }

    public AccountEventType Event { get; private set; }

    public string? Detail { get; private set; }

    /// <summary>Who did it (the signed-in owner's e-mail address).</summary>
    public string Actor { get; private set; }

    public static AccountEvent Record(AccountEventType type, Guid? accountId, string? detail, string actor, DateTimeOffset now) =>
        new(Guid.CreateVersion7(now), now, accountId, type, Limit(detail, MaxDetailLength), Limit(actor, MaxActorLength) ?? "unknown");

    private static string? Limit(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
