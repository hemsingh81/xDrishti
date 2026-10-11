namespace XDrishti.Application.Abstractions.Accounts;

/// <summary>Deployment settings the Dhan login depends on.</summary>
public interface IBrokerSettings
{
    /// <summary>
    /// Where the owner's browser reaches xDrishti (for example <c>http://localhost:8080</c>, no trailing slash). Dhan redirects
    /// back to it after a login, so it is part of the redirect URL registered with the Dhan app.
    /// </summary>
    string PublicBaseUrl { get; }
}
