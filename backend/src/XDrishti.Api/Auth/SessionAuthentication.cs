namespace XDrishti.Api.Auth;

/// <summary>Names that tie the cookie, the authentication scheme and the request pipeline together.</summary>
internal static class SessionAuthentication
{
    public const string Scheme = "xd-session";

    /// <summary>The session cookie: an opaque random token (HttpOnly, SameSite=Strict); only its hash is stored server-side.</summary>
    public const string CookieName = "xd.sid";

    public const string CsrfHeader = "X-CSRF-Token";

    public const string SessionIdClaim = "xd:sid";

    public const string LoginRateLimitPolicy = "login";

    public const string CsrfInvalidCode = "auth.csrf_invalid";

    public const string RateLimitedCode = "auth.rate_limited";
}
