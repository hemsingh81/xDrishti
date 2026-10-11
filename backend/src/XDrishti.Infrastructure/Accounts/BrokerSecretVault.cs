using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Persistence;

namespace XDrishti.Infrastructure.Accounts;

/// <summary>
/// Broker secrets encrypted in <c>accounts.broker_secrets</c>. Writes are tracked by the scoped context and saved by the accounts unit of
/// work together with the account change. Only account ids and secret names are ever logged.
/// </summary>
internal sealed partial class BrokerSecretVault(XDrishtiDbContext db, BrokerSecretCipher cipher, ILogger<BrokerSecretVault> logger) : IBrokerSecretVault
{
    public async ValueTask SetAsync(Guid accountId, BrokerSecretName name, SecretValue value, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var key = BrokerSecretNames.Key(name);
        var stored = cipher.Encrypt(accountId, key, value);
        var record = await db.BrokerSecrets.FindAsync([accountId, key], cancellationToken);
        if (record is null)
        {
            db.BrokerSecrets.Add(new BrokerSecretRecord(accountId, key, stored, BrokerSecretCipher.CurrentKeyVersion, now));
        }
        else
        {
            record.Replace(stored, BrokerSecretCipher.CurrentKeyVersion, now);
        }
    }

    public async ValueTask<SecretValue?> GetAsync(Guid accountId, BrokerSecretName name, CancellationToken cancellationToken)
    {
        var key = BrokerSecretNames.Key(name);
        var record = await db.BrokerSecrets.AsNoTracking().FirstOrDefaultAsync(x => x.AccountId == accountId && x.Name == key, cancellationToken);
        if (record is null)
        {
            return null;
        }

        if (cipher.TryDecrypt(accountId, key, record.KeyVersion, record.Ciphertext, out var value))
        {
            return value;
        }

        LogUndecryptable(logger, accountId, key, record.KeyVersion);
        return null;
    }

    public async ValueTask DeleteAsync(Guid accountId, BrokerSecretName name, CancellationToken cancellationToken)
    {
        var record = await db.BrokerSecrets.FindAsync([accountId, BrokerSecretNames.Key(name)], cancellationToken);
        if (record is not null)
        {
            db.BrokerSecrets.Remove(record);
        }
    }

    public async ValueTask<IReadOnlyDictionary<Guid, IReadOnlyList<BrokerSecretInfo>>> ListAsync(
        IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(accountIds);
        if (accountIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<BrokerSecretInfo>>();
        }

        // Metadata only: the ciphertext is not even loaded.
        var rows = await db.BrokerSecrets.AsNoTracking()
            .Where(x => accountIds.Contains(x.AccountId))
            .Select(x => new { x.AccountId, x.Name, x.UpdatedAt })
            .ToListAsync(cancellationToken);
        var result = new Dictionary<Guid, IReadOnlyList<BrokerSecretInfo>>();
        foreach (var group in rows.GroupBy(r => r.AccountId))
        {
            var items = new List<BrokerSecretInfo>();
            foreach (var row in group)
            {
                if (BrokerSecretNames.TryParseKey(row.Name, out var name))
                {
                    items.Add(new BrokerSecretInfo(name, row.UpdatedAt));
                }
            }

            result[group.Key] = items;
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Broker secret {Name} of account {AccountId} could not be decrypted (key version {KeyVersion}); was the vault key changed? Replace the secret")]
    private static partial void LogUndecryptable(ILogger logger, Guid accountId, string name, int keyVersion);
}
