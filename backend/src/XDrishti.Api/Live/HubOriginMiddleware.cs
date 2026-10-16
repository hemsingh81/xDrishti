using XDrishti.Api.Auth;

namespace XDrishti.Api.Live;

/// <summary>
/// Same origin only for the hub (ADR 0024 §7): a request to <c>/hubs</c> that carries an <c>Origin</c> header (every browser sends one on a WebSocket handshake and on a cross-origin POST) must name the host the request was sent to; any other
/// origin gets 403. A request without one is not from a browser page and has no cookie of a browser's other tabs to ride on (the session cookie is SameSite=Lax and the hub also needs it). No allow-list, no wildcard.
/// </summary>
internal sealed class HubOriginMiddleware(RequestDelegate next)
{
    public const string ForbiddenCode = "hub.origin_forbidden";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase) && !SameOrigin(context))
        {
            await ProblemResults.WriteAsync(context, ProblemResults.Create(StatusCodes.Status403Forbidden, "The request comes from another origin.", ForbiddenCode));
            return;
        }

        await next(context);
    }

    private static bool SameOrigin(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("Origin", out var values) || values.Count == 0)
        {
            return true;
        }

        if (values.Count > 1 || !Uri.TryCreate(values[0], UriKind.Absolute, out var origin) || origin.PathAndQuery != "/")
        {
            return false;
        }

        var host = context.Request.Host;
        return host.HasValue
            && string.Equals(origin.Host, host.Host, StringComparison.OrdinalIgnoreCase)
            && string.Equals(origin.Scheme, context.Request.Scheme, StringComparison.OrdinalIgnoreCase)
            && (host.Port ?? DefaultPort(context.Request.Scheme)) == origin.Port;
    }

    private static int DefaultPort(string scheme) => string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80;
}
