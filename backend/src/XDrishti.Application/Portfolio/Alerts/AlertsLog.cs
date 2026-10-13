using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Portfolio.Alerts;

/// <summary>Structured log events of the price alerts (event ids 4061 to 4069). Alert ids, symbols and counts only — never levels, prices or notes.</summary>
internal static partial class AlertsLog
{
    [LoggerMessage(EventId = 4061, Level = LogLevel.Information, Message = "Price alert {AlertId} created on {Symbol}")]
    public static partial void Created(ILogger logger, Guid alertId, string symbol);

    [LoggerMessage(EventId = 4062, Level = LogLevel.Information, Message = "Price alert {AlertId} on {Symbol} triggered")]
    public static partial void Triggered(ILogger logger, Guid alertId, string symbol);

    [LoggerMessage(EventId = 4063, Level = LogLevel.Information, Message = "Price alert {AlertId} cancelled")]
    public static partial void Cancelled(ILogger logger, Guid alertId);

    [LoggerMessage(EventId = 4064, Level = LogLevel.Information, Message = "Price alerts checked: {Active} active, {Quoted} quoted, {Triggered} triggered")]
    public static partial void Checked(ILogger logger, int active, int quoted, int triggered);
}
