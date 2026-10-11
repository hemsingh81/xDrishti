namespace XDrishti.Infrastructure.Accounts;

/// <summary>
/// One row of <c>accounts.broker_secrets</c>: an encrypted secret of an account (<see cref="BrokerSecretCipher"/>). Name is the storage key
/// of the secret (<c>api_key</c>, …); Ciphertext holds nonce, tag and encrypted value; KeyVersion says which vault key encrypted it.
/// </summary>
internal sealed class BrokerSecretRecord
{
    // Parameter names match property names so EF Core can bind this constructor when materialising.
    public BrokerSecretRecord(Guid accountId, string name, byte[] ciphertext, int keyVersion, DateTimeOffset updatedAt)
    {
        AccountId = accountId;
        Name = name;
        Ciphertext = ciphertext;
        KeyVersion = keyVersion;
        UpdatedAt = updatedAt;
    }

    public Guid AccountId { get; private set; }

    public string Name { get; private set; }

    public byte[] Ciphertext { get; private set; }

    public int KeyVersion { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void Replace(byte[] ciphertext, int keyVersion, DateTimeOffset updatedAt)
    {
        Ciphertext = ciphertext;
        KeyVersion = keyVersion;
        UpdatedAt = updatedAt;
    }
}
