using Serilog.Events;

namespace XDrishti.Api.Infrastructure;

/// <summary>Keeps request logs useful: health probes are verbose-only, errors are errors.</summary>
internal static class RequestLogLevel
{
    public static LogEventLevel ForPath(HttpContext context, double elapsedMs, Exception? exception)
    {
        if (exception is not null || context.Response.StatusCode >= 500)
        {
            return LogEventLevel.Error;
        }

        // Probes and the proxy's per-request session check are noise: verbose only.
        var quiet = context.Request.Path.StartsWithSegments("/health") || context.Request.Path.StartsWithSegments("/api/auth/verify");
        return quiet ? LogEventLevel.Verbose : LogEventLevel.Information;
    }
}
