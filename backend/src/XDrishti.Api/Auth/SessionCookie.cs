namespace XDrishti.Api.Auth;

/// <summary>Writes and clears the session cookie with the flags required by the spec.</summary>
internal static class SessionCookie
{
    public static void Write(HttpContext context, string sessionToken, DateTimeOffset expiresAt) =>
        context.Response.Cookies.Append(SessionAuthentication.CookieName, sessionToken, Options(context, expiresAt));

    public static void Clear(HttpContext context) =>
        context.Response.Cookies.Delete(SessionAuthentication.CookieName, Options(context, null));

    // Secure whenever the request (as seen through the trusted proxy) is HTTPS; the local stack is plain HTTP.
    // SameSite=Lax (ADR 0008): the brokers send the owner's browser back to /api/brokers/<broker>/callback with a top-level GET from their
    // site, and that callback needs the session. Lax still keeps the cookie off cross-site POST/PUT/DELETE and sub-requests; every
    // state-changing request also needs the X-CSRF-Token header, and GET endpoints change nothing except the one-time broker callbacks.
    private static CookieOptions Options(HttpContext context, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Secure = context.Request.IsHttps,
        IsEssential = true,
        Expires = expires,
    };
}
