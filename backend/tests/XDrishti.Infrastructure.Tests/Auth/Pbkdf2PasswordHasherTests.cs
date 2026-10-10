using XDrishti.Application.Abstractions.Auth;
using XDrishti.Infrastructure.Auth;

namespace XDrishti.Infrastructure.Tests.Auth;

public sealed class Pbkdf2PasswordHasherTests
{
    // Generated per run: no literal credential in the repository.
    private static readonly string Password = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(18));

    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void Hash_uses_pbkdf2_sha512_with_at_least_210000_iterations()
    {
        var parts = _hasher.Hash(Password).Split('$');

        parts[1].ShouldBe("pbkdf2-sha512");
        int.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture).ShouldBeGreaterThanOrEqualTo(210_000);
    }

    [Fact]
    public void Hash_does_not_contain_the_password_and_is_salted()
    {
        var first = _hasher.Hash(Password);
        var second = _hasher.Hash(Password);

        first.ShouldNotContain(Password);
        first.ShouldNotBe(second);
    }

    [Fact]
    public void Correct_password_verifies()
    {
        var hash = _hasher.Hash(Password);

        _hasher.Verify(hash, Password).ShouldBe(PasswordVerificationResult.Success);
    }

    [Fact]
    public void Wrong_password_fails()
    {
        var hash = _hasher.Hash(Password);

        _hasher.Verify(hash, Password + "x").ShouldBe(PasswordVerificationResult.Failed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain text")]
    [InlineData("$pbkdf2-sha512$abc$AAAA$AAAA")]
    [InlineData("$pbkdf2-sha512$0$AAAA$AAAA")]
    [InlineData("$pbkdf2-sha512$210000$!!!$AAAA")]
    [InlineData("$other$210000$AAAA$AAAA")]
    public void Malformed_hashes_fail_instead_of_throwing(string stored) =>
        _hasher.Verify(stored, Password).ShouldBe(PasswordVerificationResult.Failed);

    [Fact]
    public void A_hash_with_fewer_iterations_verifies_but_asks_for_an_upgrade()
    {
        var weak = new Pbkdf2PasswordHasher(1_000).Hash(Password);

        _hasher.Verify(weak, Password).ShouldBe(PasswordVerificationResult.SuccessRehashNeeded);
        _hasher.Verify(weak, Password + "x").ShouldBe(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void Simulated_verification_does_not_throw() =>
        Should.NotThrow(() => _hasher.SimulateVerification(Password));
}
