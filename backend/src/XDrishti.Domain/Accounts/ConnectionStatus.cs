namespace XDrishti.Domain.Accounts;

/// <summary>
/// The health of an account's broker connection. Stored by name — never rename a value. <see cref="Expired"/> is never stored: a connected
/// token counts as expired from its expiry time on (<see cref="BrokerConnection.StatusAt"/>).
/// </summary>
public enum ConnectionStatus
{
    NotConnected,
    Connected,
    Expired,
}
