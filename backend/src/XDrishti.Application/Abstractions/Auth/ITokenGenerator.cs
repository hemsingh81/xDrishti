namespace XDrishti.Application.Abstractions.Auth;

/// <summary>Opaque random tokens and their one-way hashes (only hashes are stored).</summary>
public interface ITokenGenerator
{
    /// <summary>A new unguessable session token (cryptographically secure random).</summary>
    string NewSessionToken();

    /// <summary>The CSRF token that belongs to a session token; recomputable while the session cookie is present.</summary>
    string DeriveCsrfToken(string sessionToken);

    /// <summary>SHA-256 of a token, as stored in the database.</summary>
    string Hash(string token);
}
