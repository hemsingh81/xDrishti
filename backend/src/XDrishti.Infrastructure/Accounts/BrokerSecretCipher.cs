using System.Diagnostics.CodeAnalysis;
using XDrishti.Domain.Accounts;

namespace XDrishti.Infrastructure.Accounts;

/// <summary>
/// AES-256-GCM for broker secrets (ADR 0008) over the shared <see cref="SecretCipher"/>. The purpose picks the derived key: the vault (default, unchanged since ADR 0008)
/// or the execution hand-over (ADR 0171), each from its own key material. The associated data binds account and name.
/// </summary>
internal sealed class BrokerSecretCipher(string keyMaterial, SecretCipherPurpose purpose = SecretCipherPurpose.BrokerVault)
{
    /// <summary>Version of the key that encrypts new values (stored per row as key_version, so the key can be rotated later).</summary>
    public const int CurrentKeyVersion = SecretCipher.CurrentKeyVersion;

    public const int NonceSize = SecretCipher.NonceSize;

    public const int TagSize = SecretCipher.TagSize;

    private readonly SecretCipher _core = new(keyMaterial, purpose);

    public byte[] Encrypt(Guid accountId, string name, SecretValue value) => _core.Encrypt(accountId, name, value);

    /// <summary>False when the bytes were not written for this account and name, with this key (version), or were changed.</summary>
    public bool TryDecrypt(Guid accountId, string name, int keyVersion, ReadOnlySpan<byte> stored, [NotNullWhen(true)] out SecretValue? value) =>
        _core.TryDecrypt(accountId, name, keyVersion, stored, out value);
}
