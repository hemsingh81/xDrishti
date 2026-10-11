using XDrishti.Application.Auth;

namespace XDrishti.Api.Auth;

internal static class HttpContextAuthExtensions
{
    private static readonly object VerifiedSessionKey = new();
    private static readonly object FailureCodeKey = new();

    public static ClientInfo GetClientInfo(this HttpContext context) =>
        new(context.Connection.RemoteIpAddress?.ToString(), context.Request.Headers.UserAgent.ToString());

    /// <summary>Who is acting, for audit entries: the signed-in owner's e-mail address. Only call on endpoints that require a session.</summary>
    public static string GetActor(this HttpContext context) =>
        context.GetVerifiedSession()?.Info.User.Email ?? "unknown";

    /// <summary>The verified session of this request, set by the authentication handler; null when not signed in.</summary>
    public static VerifiedSession? GetVerifiedSession(this HttpContext context) =>
        context.Items.TryGetValue(VerifiedSessionKey, out var value) ? value as VerifiedSession : null;

    public static void SetVerifiedSession(this HttpContext context, VerifiedSession session) =>
        context.Items[VerifiedSessionKey] = session;

    /// <summary>Why a presented session cookie was refused (for the 401 body); null when none was presented.</summary>
    public static string? GetAuthFailureCode(this HttpContext context) =>
        context.Items.TryGetValue(FailureCodeKey, out var value) ? value as string : null;

    public static void SetAuthFailureCode(this HttpContext context, string code) =>
        context.Items[FailureCodeKey] = code;
}
