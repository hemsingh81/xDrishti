using System.Text;
using XDrishti.Domain.Accounts;
using XDrishti.Infrastructure.Accounts;

namespace XDrishti.Infrastructure.Tests.Accounts;

/// <summary>AES-256-GCM with the account and secret name as associated data (ADR 0008). Keys and values are made up.</summary>
public sealed class BrokerSecretCipherTests
{
    private const string Marker = "FAKE-SECRET-MARKER-DO-NOT-LEAK";

    private const string Key = "test-vault-key-0123456789abcdefghijklmnop";

    private static readonly Guid Account = Guid.Parse("01890000-0000-7000-8000-000000000001");

    private static readonly Guid OtherAccount = Guid.Parse("01890000-0000-7000-8000-000000000002");

    private readonly BrokerSecretCipher _cipher = new(Key);

    private static SecretValue Secret(string text = Marker) =>
        SecretValue.TryCreate(text, out var value, out _) ? value : throw new InvalidOperationException("invalid test value");

    [Fact]
    public void A_value_decrypts_for_the_account_and_name_it_was_written_for()
    {
        var stored = _cipher.Encrypt(Account, "api_key", Secret());

        _cipher.TryDecrypt(Account, "api_key", BrokerSecretCipher.CurrentKeyVersion, stored, out var value).ShouldBeTrue();
        value.Reveal().ShouldBe(Marker);
    }

    [Fact]
    public void The_stored_bytes_are_nonce_tag_and_ciphertext_and_hold_no_plain_text()
    {
        var stored = _cipher.Encrypt(Account, "api_key", Secret());

        stored.Length.ShouldBe(BrokerSecretCipher.NonceSize + BrokerSecretCipher.TagSize + Marker.Length);
        Encoding.UTF8.GetString(stored).ShouldNotContain(Marker);
        Convert.ToHexString(stored).ShouldNotContain(Convert.ToHexString(Encoding.UTF8.GetBytes(Marker)));
    }

    [Fact]
    public void Every_encryption_uses_a_new_nonce()
    {
        var all = Enumerable.Range(0, 100).Select(_ => _cipher.Encrypt(Account, "api_key", Secret())).ToList();

        all.Select(s => Convert.ToHexString(s.AsSpan(0, BrokerSecretCipher.NonceSize))).Distinct().Count().ShouldBe(100);
        all.Select(Convert.ToHexString).Distinct().Count().ShouldBe(100);
    }

    [Fact]
    public void Another_account_cannot_decrypt_it() =>
        _cipher.TryDecrypt(OtherAccount, "api_key", 1, _cipher.Encrypt(Account, "api_key", Secret()), out _).ShouldBeFalse();

    [Fact]
    public void Another_secret_name_cannot_decrypt_it() =>
        _cipher.TryDecrypt(Account, "api_secret", 1, _cipher.Encrypt(Account, "api_key", Secret()), out _).ShouldBeFalse();

    [Fact]
    public void Another_vault_key_cannot_decrypt_it()
    {
        var other = new BrokerSecretCipher(Key + "x");

        other.TryDecrypt(Account, "api_key", 1, _cipher.Encrypt(Account, "api_key", Secret()), out _).ShouldBeFalse();
    }

    [Fact]
    public void The_same_key_material_derives_the_same_key()
    {
        var again = new BrokerSecretCipher(Key);

        again.TryDecrypt(Account, "api_key", 1, _cipher.Encrypt(Account, "api_key", Secret()), out var value).ShouldBeTrue();
        value.Reveal().ShouldBe(Marker);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(BrokerSecretCipher.NonceSize)]
    [InlineData(BrokerSecretCipher.NonceSize + BrokerSecretCipher.TagSize + 3)]
    public void Changed_bytes_are_refused(int position)
    {
        var stored = _cipher.Encrypt(Account, "api_key", Secret());
        stored[position] ^= 0x01;

        _cipher.TryDecrypt(Account, "api_key", 1, stored, out var value).ShouldBeFalse();
        value.ShouldBeNull();
    }

    [Fact]
    public void An_unknown_key_version_or_truncated_bytes_are_refused()
    {
        var stored = _cipher.Encrypt(Account, "api_key", Secret());

        _cipher.TryDecrypt(Account, "api_key", 2, stored, out _).ShouldBeFalse();
        _cipher.TryDecrypt(Account, "api_key", 1, stored.AsSpan(0, 20), out _).ShouldBeFalse();
    }

    [Fact]
    public void A_decrypted_value_never_shows_in_its_text_form()
    {
        _cipher.TryDecrypt(Account, "api_key", 1, _cipher.Encrypt(Account, "api_key", Secret()), out var value).ShouldBeTrue();

        value.ToString().ShouldNotContain(Marker);
        $"{value}".ShouldBe(SecretValue.Redacted);
    }

    [Fact]
    public void Long_values_such_as_tokens_round_trip()
    {
        var token = string.Concat(Enumerable.Repeat("eyJ0eXAiOiJKV1Qi.", 100));

        _cipher.TryDecrypt(Account, "access_token", 1, _cipher.Encrypt(Account, "access_token", Secret(token)), out var value).ShouldBeTrue();
        value.Reveal().ShouldBe(token);
    }
}
