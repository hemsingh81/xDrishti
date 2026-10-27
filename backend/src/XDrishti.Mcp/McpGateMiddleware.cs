using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace XDrishti.Mcp;

/// <summary>
/// Everything that happens to a request for <c>/mcp</c> before the MCP SDK sees it (spec P9-02 AC 11): the bearer token (SHA-256 of both sides compared in constant time, every configured token examined whatever the outcome, no hint whether it was missing or wrong), the 64 KB body cap and
/// the rate limit per client (fixed one-minute window). Any other path passes through untouched (<c>/health</c> is anonymous and carries no detail). The host-header check is the framework's host filtering (<c>AllowedHosts: xd-mcp</c>).
/// </summary>
internal sealed class McpGateMiddleware(RequestDelegate next, IOptions<McpOptions> options, TimeProvider time, ILogger<McpGateMiddleware> logger)
{
    private const long MaxBodyBytes = 64 * 1024;

    private readonly object _gate = new();
    private readonly Dictionary<string, (long Minute, int Count)> _windows = new(StringComparer.Ordinal);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/mcp", StringComparison.Ordinal))
        {
            await next(context);
            return;
        }

        var client = Authenticate(context.Request.Headers.Authorization.ToString());
        if (client is null)
        {
            logger.Unauthorized();
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return;
        }

        if (context.Request.ContentLength > MaxBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return;
        }

        var sizeLimit = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = MaxBodyBytes;
        }

        if (!TryCount(client))
        {
            logger.RateLimited(client);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = "60";
            return;
        }

        await next(context);
    }

    /// <summary>The client whose token this is, or null. Constant time over all configured tokens; an empty configured token never matches.</summary>
    private string? Authenticate(string header)
    {
        const string Prefix = "Bearer ";
        var provided = header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? header[Prefix.Length..].Trim() : string.Empty;
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        string? matched = null;
        foreach (var (client, token) in options.Value.Tokens)
        {
            var same = CryptographicOperations.FixedTimeEquals(providedHash, SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
            if (same && !string.IsNullOrEmpty(token) && provided.Length > 0)
            {
                matched ??= client;
            }
        }

        return matched;
    }

    private bool TryCount(string client)
    {
        var minute = time.GetUtcNow().ToUnixTimeSeconds() / 60;
        lock (_gate)
        {
            var (window, count) = _windows.GetValueOrDefault(client, (minute, 0));
            if (window != minute)
            {
                (window, count) = (minute, 0);
            }

            if (count >= options.Value.RateLimitPerMinute)
            {
                _windows[client] = (window, count);
                return false;
            }

            _windows[client] = (window, count + 1);
            return true;
        }
    }
}
