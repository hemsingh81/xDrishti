namespace XDrishti.Domain.Accounts;

/// <summary>
/// When a Dhan access token stops working, and the owner's calendar day (ADR 0008/0009). Results are UTC instants (the database stores
/// UTC); the rules themselves are in India time, which has no daylight saving.
/// </summary>
public static class BrokerTokenExpiry
{
    /// <summary>India Standard Time, UTC+05:30.</summary>
    public static readonly TimeSpan IndiaOffset = TimeSpan.FromMinutes(330);

    /// <summary>How long a Dhan token lives when Dhan does not say (24 hours).</summary>
    public static readonly TimeSpan DhanDefaultLifetime = TimeSpan.FromHours(24);

    /// <summary>
    /// The expiry of a token issued at <paramref name="issuedAt"/>: the time Dhan states (when it lies after the issue), otherwise
    /// <see cref="DhanDefaultLifetime"/> after the issue.
    /// </summary>
    public static DateTimeOffset Of(DateTimeOffset issuedAt, DateTimeOffset? statedByBroker) =>
        (statedByBroker is { } stated && stated > issuedAt ? stated : issuedAt + DhanDefaultLifetime).ToUniversalTime();

    /// <summary>The Indian calendar day that contains <paramref name="now"/>, as the UTC instants [start, end).</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) IndianDay(DateTimeOffset now)
    {
        var local = now.ToOffset(IndiaOffset);
        var start = new DateTimeOffset(local.Date, IndiaOffset).ToUniversalTime();
        return (start, start.AddDays(1));
    }
}
