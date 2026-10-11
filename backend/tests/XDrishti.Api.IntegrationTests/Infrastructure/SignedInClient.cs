using XDrishti.Application.Auth;

namespace XDrishti.Api.IntegrationTests.Infrastructure;

/// <summary>An HTTP client that carries a valid session cookie and CSRF header, plus what the login returned.</summary>
public sealed record SignedInClient(HttpClient Client, SessionInfo Session, string CookieValue)
{
    public string CookieHeader => $"xd.sid={CookieValue}";
}
