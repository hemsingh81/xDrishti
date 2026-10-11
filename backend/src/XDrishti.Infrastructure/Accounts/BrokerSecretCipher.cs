using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Domain.Accounts;

namespace XDrishti.Infrastructure.Accounts;

/// <summary>
/// AES-256-GCM for broker secrets (ADR 0008). The 256-bit key is derived with HKDF-SHA256 from the configured vault key. Every encryption
/// uses a fresh random 96-bit nonce; the stored bytes are <c>nonce (12) ‖ tag (16) ‖ ciphertext</c>. The associated data is the account id
/// (16 bytes, big-endian) followed by the secret's storage key, so a ciphertext only decrypts for the account and name it was written for.
/// </summary>
internal sealed class BrokerSecretCipher
{
    /// <summary>Version of the vault key that encrypts new values (stored per row as key_version, so the key can be rotated later).</summary>
    public const int CurrentKeyVersion = 1;

    public const int NonceSize = 12;

    public const int TagSize = 16;

    private const int KeySize = 32;

    private static readonly byte[] Salt = "xdrishti/broker-vault"u8.ToArray();

    private static readonly byte[] Info = "aes-256-gcm/key-v1"u8.ToArray();

    private readonly byte[] _key;

    public BrokerSecretCipher(string keyMaterial)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyMaterial);
        var input = Encoding.UTF8.GetBytes(keyMaterial);
        try
        {
            _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, input, KeySize, Salt, Info);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    public byte[] Encrypt(Guid accountId, string name, SecretValue value)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        var plaintext = Encoding.UTF8.GetBytes(value.Reveal());
        try
        {
            var stored = new byte[NonceSize + TagSize + plaintext.Length];
            var nonce = stored.AsSpan(0, NonceSize);
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(_key, TagSize);
            aes.Encrypt(nonce, plaintext, stored.AsSpan(NonceSize + TagSize), stored.AsSpan(NonceSize, TagSize), AssociatedData(accountId, name));
            return stored;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>False when the bytes were not written for this account and name, with this key (version), or were changed.</summary>
    public bool TryDecrypt(Guid accountId, string name, int keyVersion, ReadOnlySpan<byte> stored, [NotNullWhen(true)] out SecretValue? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        value = null;
        if (keyVersion != CurrentKeyVersion || stored.Length < NonceSize + TagSize)
        {
            return false;
        }

        var plaintext = new byte[stored.Length - NonceSize - TagSize];
        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(stored[..NonceSize], stored[(NonceSize + TagSize)..], stored.Slice(NonceSize, TagSize), plaintext, AssociatedData(accountId, name));
            return SecretValue.TryCreate(Encoding.UTF8.GetString(plaintext), out value, out _);
        }
        catch (AuthenticationTagMismatchException)
        {
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] AssociatedData(Guid accountId, string name)
    {
        var data = new byte[16 + Encoding.UTF8.GetByteCount(name)];
        accountId.TryWriteBytes(data.AsSpan(0, 16), bigEndian: true, out _);
        Encoding.UTF8.GetBytes(name, data.AsSpan(16));
        return data;
    }
}
