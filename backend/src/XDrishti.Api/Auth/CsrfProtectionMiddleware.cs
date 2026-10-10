using System.Security.Cryptography;
using System.Text;

namespace XDrishti.Api.Auth;

/// <summary>
/// State-changing requests (POST/PUT/PATCH/DELETE) of a signed-in session must carry the session's CSRF token in
/// <c>X-CSRF-Token</c>; otherwise 403. Runs after authentication and before authorization, so an unauthenticated
/// request still gets its 401 first.
/// </summary>
internal sealed class CsrfProtectionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresToken(context) && context.GetVerifiedSession() is { } session && !Matches(context, session.Info.CsrfToken))
        {
            await ProblemResults.WriteAsync(context, ProblemResults.Create(
                StatusCodes.Status403Forbidden, "The CSRF token is missing or invalid.", SessionAuthentication.CsrfInvalidCode));
            return;
        }

        await next(context);
    }

    private static bool RequiresToken(HttpContext context) =>
        !(HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)
            || HttpMethods.IsOptions(context.Request.Method) || HttpMethods.IsTrace(context.Request.Method))
        && context.GetEndpoint()?.Metadata.GetMetadata<CsrfExemptMetadata>() is null;

    private static bool Matches(HttpContext context, string expected)
    {
        var supplied = context.Request.Headers[SessionAuthentication.CsrfHeader].ToString();
        return supplied.Length > 0
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected));
    }
}
