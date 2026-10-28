using Microsoft.Extensions.Logging;

namespace XDrishti.Application.LiveTrading;

/// <summary>Log events of the live-versus-paper report (ids 5120 to 5129); counts and milliseconds only, never a value.</summary>
internal static partial class LiveReportLog
{
    [LoggerMessage(EventId = 5120, Level = LogLevel.Information, Message = "live.report kind={Kind} rows={Rows} ms={Ms}")]
    public static partial void Served(ILogger logger, string kind, int rows, long ms);
}
