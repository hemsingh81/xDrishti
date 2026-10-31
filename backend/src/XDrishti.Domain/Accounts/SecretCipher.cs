using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Domain.Accounts;

/// <summary>
/// AES-256-GCM for a secret of one account (ADR 0008, ADR 0171). The 256-bit key is derived with HKDF-SHA256 from the key material, with a salt of its own per
/// <see cref="SecretCipherPurpose"/>. Every encryption uses a fresh random 96-bit nonce; the stored bytes are <c>nonce (12) ‖ tag (16) ‖ ciphertext</c>. The associated
/// data is the account id (16 bytes, big-endian) followed by the secret's name, so a ciphertext only decrypts for the account and name it was written for. Pure
/// computation, no I/O: shared by the vault (Infrastructure) and the execution service, which references only this assembly.
/// </summary>
public sealed class SecretCipher
{
    /// <summary>Version of the key that seals new values (stored per row as key_version).</summary>
    public const int CurrentKeyVersion = 1;

    public const int NonceSize = 12;

    public const int TagSize = 16;

    /// <summary>The shortest key material accepted by the hosts (characters).</summary>
    public const int MinKeyLength = 32;

    private const int KeySize = 32;

    private static readonly byte[] Info = "aes-256-gcm/key-v1"u8.ToArray();

    private readonly byte[] _key;

    public SecretCipher(string keyMaterial, SecretCipherPurpose purpose)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyMaterial);
        var input = Encoding.UTF8.GetBytes(keyMaterial);
        try
        {
            _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, input, KeySize, Salt(purpose), Info);
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

    /// <summary>False when the bytes were not written for this account and name, with this key (version) and purpose, or were changed.</summary>
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

    // The vault's salt is the one it always had: rows written before the purpose existed decrypt unchanged.
    private static byte[] Salt(SecretCipherPurpose purpose) => purpose switch
    {
        SecretCipherPurpose.BrokerVault => "xdrishti/broker-vault"u8.ToArray(),
        SecretCipherPurpose.ExecHandover => "xdrishti/exec-handover"u8.ToArray(),
        _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
    };

    private static byte[] AssociatedData(Guid accountId, string name)
    {
        var data = new byte[16 + Encoding.UTF8.GetByteCount(name)];
        accountId.TryWriteBytes(data.AsSpan(0, 16), bigEndian: true, out _);
        Encoding.UTF8.GetBytes(name, data.AsSpan(16));
        return data;
    }
}
