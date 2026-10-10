using XDrishti.Application.Abstractions.Auth;

namespace XDrishti.Application.Tests.Auth;

/// <summary>Deterministic hasher: "hash(x)" is "h:x". Remembers how often the dummy verification ran.</summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public int SimulatedVerifications { get; private set; }

    /// <summary>Hashes produced by this prefix are reported as outdated.</summary>
    public string OutdatedPrefix { get; init; } = "old:";

    public string Hash(string password) => "h:" + password;

    public PasswordVerificationResult Verify(string passwordHash, string password)
    {
        if (passwordHash.StartsWith(OutdatedPrefix, StringComparison.Ordinal))
        {
            return passwordHash == OutdatedPrefix + password ? PasswordVerificationResult.SuccessRehashNeeded : PasswordVerificationResult.Failed;
        }

        return passwordHash == Hash(password) ? PasswordVerificationResult.Success : PasswordVerificationResult.Failed;
    }

    public void SimulateVerification(string password) => SimulatedVerifications++;
}

internal sealed class FakeTokenGenerator : ITokenGenerator
{
    private int _counter;

    public string NewSessionToken() => "token-" + ++_counter;

    public string DeriveCsrfToken(string sessionToken) => "csrf-of-" + sessionToken;

    public string Hash(string token) => "hash-of-" + token;
}
