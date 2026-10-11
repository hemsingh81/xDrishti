using XDrishti.Domain.Common;

namespace XDrishti.Domain.Auth;

/// <summary>One entry of the authentication audit trail. Never contains passwords or tokens.</summary>
public sealed class AuditEvent : Entity<Guid>
{
    public const int MaxEmailLength = 256;

    public const int MaxIpLength = 64;

    public const int MaxUserAgentLength = 256;

    public const int MaxDetailLength = 512;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private AuditEvent(Guid id, DateTimeOffset at, AuditEventType @event, string? email, string? ip, string? userAgent, string? detail)
        : base(id)
    {
        At = at;
        Event = @event;
        Email = email;
        Ip = ip;
        UserAgent = userAgent;
        Detail = detail;
    }

    public DateTimeOffset At { get; private set; }

    public AuditEventType Event { get; private set; }

    /// <summary>The e-mail address that was used (attempted), as typed but trimmed and length-limited.</summary>
    public string? Email { get; private set; }

    public string? Ip { get; private set; }

    public string? UserAgent { get; private set; }

    public string? Detail { get; private set; }

    /// <summary>Creates an entry; free-text values are cut to their column size (they come from the outside).</summary>
    public static AuditEvent Record(AuditEventType type, DateTimeOffset now, string? email, string? ip, string? userAgent, string? detail = null) =>
        new(Guid.CreateVersion7(now), now, type, Limit(email, MaxEmailLength), Limit(ip, MaxIpLength), Limit(userAgent, MaxUserAgentLength), Limit(detail, MaxDetailLength));

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
