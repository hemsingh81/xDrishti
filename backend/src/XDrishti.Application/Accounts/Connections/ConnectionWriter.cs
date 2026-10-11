using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Accounts.Connections;

/// <summary>The steps of connecting an account: reading the app credentials, storing the token, clearing a connection and noting failures.</summary>
internal sealed class ConnectionWriter(IBrokerSecretVault vault, IBrokerConnectionStore connections, IAccountEventLog events)
{
    /// <summary>The account's API key and secret; null when either is not set (or cannot be decrypted).</summary>
    public async ValueTask<BrokerAppCredentials?> AppAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var key = await vault.GetAsync(accountId, BrokerSecretName.ApiKey, cancellationToken);
        var secret = await vault.GetAsync(accountId, BrokerSecretName.ApiSecret, cancellationToken);
        return key is null || secret is null ? null : new BrokerAppCredentials(key, secret);
    }

    /// <summary>The account's connection, tracked; a new not-connected one when it never had one.</summary>
    public async ValueTask<BrokerConnection> ConnectionAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var connection = await connections.FindAsync(accountId, cancellationToken);
        if (connection is null)
        {
            connection = BrokerConnection.NotConnected(accountId);
            connections.Add(connection);
        }

        return connection;
    }

    /// <summary>Stores the token encrypted and marks the account connected until Dhan's expiry (or 24 hours when Dhan states none).</summary>
    public async ValueTask<BrokerConnection> StoreAsync(BrokerToken token, Guid accountId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await vault.SetAsync(accountId, BrokerSecretName.AccessToken, token.AccessToken, now, cancellationToken);
        var connection = await ConnectionAsync(accountId, cancellationToken);
        connection.Connect(BrokerTokenExpiry.Of(now, token.ExpiresAt), token.BrokerUserId, token.BrokerUserName, now);
        return connection;
    }

    /// <summary>Removes the stored access token.</summary>
    public async ValueTask DeleteTokenAsync(Guid accountId, CancellationToken cancellationToken) =>
        await vault.DeleteAsync(accountId, BrokerSecretName.AccessToken, cancellationToken);

    /// <summary>
    /// Forgets the account's connection: deletes the stored token and resets the connection to not connected (no broker call). Returns
    /// false when there was nothing to clear (no token and no connected or expired connection).
    /// </summary>
    public async ValueTask<bool> ClearAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var stored = (await vault.ListAsync([accountId], cancellationToken)).GetValueOrDefault(accountId) ?? [];
        var hasToken = stored.Any(s => s.Name == BrokerSecretName.AccessToken);
        var connection = await connections.FindAsync(accountId, cancellationToken);
        if (!hasToken && connection is null or { Status: ConnectionStatus.NotConnected })
        {
            return false;
        }

        await DeleteTokenAsync(accountId, cancellationToken);
        connection?.Disconnect();
        return true;
    }

    /// <summary>Notes a failed sign-in on the connection (an existing token stays) and in the audit trail.</summary>
    public async ValueTask RecordFailureAsync(Guid accountId, string reason, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var connection = await ConnectionAsync(accountId, cancellationToken);
        connection.ConnectFailed(reason);
        events.Record(AccountEvent.Record(AccountEventType.BrokerConnectFailed, accountId, reason, actor, now));
    }
}
