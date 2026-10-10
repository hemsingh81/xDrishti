namespace XDrishti.Api.Auth;

/// <summary>Writes and clears the session cookie with the flags required by the spec.</summary>
internal static class SessionCookie
{
    public static void Write(HttpContext context, string sessionToken, DateTimeOffset expiresAt) =>
        context.Response.Cookies.Append(SessionAuthentication.CookieName, sessionToken, Options(context, expiresAt));

    public static void Clear(HttpContext context) =>
        context.Response.Cookies.Delete(SessionAuthentication.CookieName, Options(context, null));

    // Secure whenever the request (as seen through the trusted proxy) is HTTPS; the local stack is plain HTTP.
    private static CookieOptions Options(HttpContext context, DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        Secure = context.Request.IsHttps,
        IsEssential = true,
        Expires = expires,
    };
}
