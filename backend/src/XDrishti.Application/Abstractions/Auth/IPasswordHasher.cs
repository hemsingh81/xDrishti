namespace XDrishti.Application.Abstractions.Auth;

/// <summary>One-way, salted, deliberately slow password hashing.</summary>
public interface IPasswordHasher
{
    string Hash(string password);

    PasswordVerificationResult Verify(string passwordHash, string password);

    /// <summary>
    /// Spends the same time as a real verification without a stored hash, so a login attempt for an unknown
    /// e-mail address cannot be told apart by its response time.
    /// </summary>
    void SimulateVerification(string password);
}
