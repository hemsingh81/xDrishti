using System.Security.Cryptography;
using XDrishti.Application.Abstractions.Auth;

namespace XDrishti.Infrastructure.Auth;

/// <summary>
/// PBKDF2-HMAC-SHA512 with a random 128-bit salt, from the framework's own crypto (no extra package).
/// Stored format: <c>$pbkdf2-sha512$&lt;iterations&gt;$&lt;salt base64&gt;$&lt;hash base64&gt;</c>, so the parameters
/// travel with the hash and can be raised later (old hashes are upgraded at the next successful login).
/// </summary>
internal sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    /// <summary>Iterations for new hashes (OWASP password-storage guidance for PBKDF2-HMAC-SHA512: at least 210,000).</summary>
    public const int CurrentIterations = 210_000;

    private const string Marker = "pbkdf2-sha512";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    private readonly string _dummyHash;

    public Pbkdf2PasswordHasher()
        : this(CurrentIterations)
    {
    }

    /// <summary>Hashes with a different cost; only for tests that need an "outdated" hash.</summary>
    internal Pbkdf2PasswordHasher(int iterations)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(iterations, 1);
        Iterations = iterations;
        _dummyHash = Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)));
    }

    private int Iterations { get; }

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA512, HashBytes);
        return $"${Marker}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public PasswordVerificationResult Verify(string passwordHash, string password)
    {
        if (!TryParse(passwordHash, out var iterations, out var salt, out var expected))
        {
            return PasswordVerificationResult.Failed;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, expected.Length);
        if (!CryptographicOperations.FixedTimeEquals(actual, expected))
        {
            return PasswordVerificationResult.Failed;
        }

        return iterations < CurrentIterations ? PasswordVerificationResult.SuccessRehashNeeded : PasswordVerificationResult.Success;
    }

    public void SimulateVerification(string password) => _ = Verify(_dummyHash, password);

    private static bool TryParse(string stored, out int iterations, out byte[] salt, out byte[] hash)
    {
        iterations = 0;
        salt = [];
        hash = [];
        var parts = stored.Split('$');
        if (parts.Length != 5 || parts[0].Length != 0 || !string.Equals(parts[1], Marker, StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out iterations) || iterations < 1)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[3]);
            hash = Convert.FromBase64String(parts[4]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length > 0 && hash.Length > 0;
    }
}
