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

        return context.Request.Path.StartsWithSegments("/health") ? LogEventLevel.Verbose : LogEventLevel.Information;
    }
}
