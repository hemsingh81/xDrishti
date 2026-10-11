using XDrishti.Domain.Accounts;

namespace XDrishti.Application.Abstractions.Accounts;

/// <summary>
/// Encrypted store of broker secrets (API key and secret, access tokens), one value per account and <see cref="BrokerSecretName"/>.
/// Writes are collected and saved by <see cref="IAccountUnitOfWork"/> together with the account change they belong to. Values only ever
/// leave it through <see cref="GetAsync"/>; implementations must never log, return or put them into exceptions.
/// </summary>
public interface IBrokerSecretVault
{
    /// <summary>Encrypts the value and stores it under the account and name, replacing an existing one.</summary>
    ValueTask SetAsync(Guid accountId, BrokerSecretName name, SecretValue value, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The decrypted value; null when it is not set or cannot be decrypted (for example with another vault key — a warning is logged).</summary>
    ValueTask<SecretValue?> GetAsync(Guid accountId, BrokerSecretName name, CancellationToken cancellationToken);

    /// <summary>Removes the value; nothing happens when it is not set.</summary>
    ValueTask DeleteAsync(Guid accountId, BrokerSecretName name, CancellationToken cancellationToken);

    /// <summary>Which secrets are set for each of the accounts and when they were last changed — never the values. Accounts without secrets are absent.</summary>
    ValueTask<IReadOnlyDictionary<Guid, IReadOnlyList<BrokerSecretInfo>>> ListAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken cancellationToken);
}
