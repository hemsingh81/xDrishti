namespace XDrishti.Domain.Accounts;

/// <summary>
/// What is known about an account's connection to its broker: status, when the token was obtained, until when it is valid and
/// whom the broker says it belongs to. The token itself lives encrypted in the broker vault (ADR 0008), never here. One per account;
/// an account without one is not connected. Times are kept in UTC.
/// </summary>
public sealed class BrokerConnection
{
    public const int MaxBrokerUserIdLength = 32;

    public const int MaxBrokerUserNameLength = 120;

    public const int MaxErrorLength = 200;

    // Parameter names match property names so EF Core can bind this constructor when materialising.
    private BrokerConnection(Guid accountId, ConnectionStatus status)
    {
        AccountId = accountId;
        Status = status;
    }

    public Guid AccountId { get; private set; }

    /// <summary>The stored status: never <see cref="ConnectionStatus.Expired"/> (see <see cref="StatusAt"/>).</summary>
    public ConnectionStatus Status { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <summary>Dhan's client id the token belongs to (shown masked).</summary>
    public string? BrokerUserId { get; private set; }

    /// <summary>The account holder's name as the broker reports it.</summary>
    public string? BrokerUserName { get; private set; }

    public DateTimeOffset? LastConnectedAt { get; private set; }

    /// <summary>A short, safe description of the last failure (never a token, secret or broker message text; at most the broker's error code).</summary>
    public string? LastError { get; private set; }

    public static BrokerConnection NotConnected(Guid accountId) => new(accountId, ConnectionStatus.NotConnected);

    /// <summary>The status at <paramref name="now"/>: a connected token is expired from its expiry time on.</summary>
    public ConnectionStatus StatusAt(DateTimeOffset now) =>
        Status == ConnectionStatus.Connected && ExpiresAt is { } expires && now >= expires ? ConnectionStatus.Expired : Status;

    /// <summary>A new token was stored (the owner signed in at Dhan).</summary>
    public void Connect(DateTimeOffset expiresAt, string brokerUserId, string? brokerUserName, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(brokerUserId);
        var userId = Clean(brokerUserId, MaxBrokerUserIdLength)!;

        // Dhan's answer may lack the holder's name: keep the one known for the same holder.
        var sameHolder = string.Equals(BrokerUserId, userId, StringComparison.OrdinalIgnoreCase);
        BrokerUserName = Clean(brokerUserName, MaxBrokerUserNameLength) ?? (sameHolder ? BrokerUserName : null);
        BrokerUserId = userId;
        Status = ConnectionStatus.Connected;
        ExpiresAt = expiresAt.ToUniversalTime();
        LastConnectedAt = now;
        LastError = null;
    }

    /// <summary>A sign-in failed; an existing token stays as it was.</summary>
    public void ConnectFailed(string error) => LastError = Clean(error, MaxErrorLength);

    /// <summary>The token was removed: back to not connected (when it last connected is kept).</summary>
    public void Disconnect()
    {
        Status = ConnectionStatus.NotConnected;
        ExpiresAt = null;
        BrokerUserId = null;
        BrokerUserName = null;
        LastError = null;
    }

    private static string? Clean(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = new string([.. value.Trim().Where(c => !char.IsControl(c))]);
        return text.Length <= max ? text : text[..max];
    }
}
