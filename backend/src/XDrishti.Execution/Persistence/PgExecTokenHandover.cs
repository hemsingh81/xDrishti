using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Npgsql;
using XDrishti.Domain.Accounts;
using XDrishti.Execution.Application.Ports;

namespace XDrishti.Execution.Persistence;

/// <summary>
/// The live readiness token source (ADR 0171): reads the account's row of <c>accounts.exec_token_handover</c> (SELECT only, as xd_exec) joined to the three columns of <c>accounts.broker_connections</c> it may read, and opens it with the hand-over key. A token
/// is given only while the connection is Connected and its expiry is after now (<see cref="TimeProvider"/>) and the row decrypts for that account and the name <c>access_token</c>; otherwise null. Never reads the vault. Only a short hash of the account and the key
/// version are ever logged.
/// </summary>
internal sealed partial class PgExecTokenHandover(NpgsqlDataSource data, IOptions<ExecHandoverKeyOptions> options, TimeProvider clock, ILogger<PgExecTokenHandover> logger) : ILiveTokenSource
{
    private static readonly string Name = BrokerSecretNames.Key(BrokerSecretName.AccessToken);

    private readonly SecretCipher? _cipher = options.Value.Key is { Length: >= SecretCipher.MinKeyLength } key ? new SecretCipher(key, SecretCipherPurpose.ExecHandover) : null;

    private int _warnedNoKey;

    public async Task<SecretValue?> FindAsync(Guid accountId, CancellationToken cancellationToken)
    {
        if (_cipher is null)
        {
            if (Interlocked.Exchange(ref _warnedNoKey, 1) == 0)
            {
                LogNoKey(logger);
            }

            return null;
        }

        byte[] sealedToken;
        int keyVersion;
        try
        {
            await using var command = data.CreateCommand(
                """
                SELECT h.sealed_token, h.key_version
                FROM accounts.exec_token_handover h
                JOIN accounts.broker_connections c ON c.account_id = h.account_id
                WHERE h.account_id = @id AND c.status = 'Connected' AND c.expires_at > @now
                """);
            command.Parameters.AddWithValue("id", accountId);
            command.Parameters.AddWithValue("now", clock.GetUtcNow());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            sealedToken = reader.GetFieldValue<byte[]>(0);
            keyVersion = reader.GetInt32(1);
        }
        catch (Exception e) when (e is NpgsqlException or InvalidOperationException or InvalidCastException)
        {
            return null;
        }

        if (_cipher.TryDecrypt(accountId, Name, keyVersion, sealedToken, out var token))
        {
            return token;
        }

        var who = Convert.ToHexStringLower(SHA256.HashData(accountId.ToByteArray()))[..8];
        LogUndecryptable(logger, who, keyVersion);
        return null;
    }

    [LoggerMessage(EventId = 6770, Level = LogLevel.Warning, Message = "execution.token_handover_no_key; every live token lookup is token_unavailable until ExecHandover__Key is set")]
    private static partial void LogNoKey(ILogger logger);

    [LoggerMessage(EventId = 6771, Level = LogLevel.Warning, Message = "execution.token_handover_undecryptable account={Account} key_version={KeyVersion}; token_unavailable until the next login (was the hand-over key changed?)")]
    private static partial void LogUndecryptable(ILogger logger, string account, int keyVersion);
}
