using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Portfolio.Actions;

/// <summary>Structured log events of the Action center (event ids 4051 to 4059). Counts and rule names only — never figures, symbols or account names.</summary>
internal static partial class ActionsLog
{
    [LoggerMessage(EventId = 4051, Level = LogLevel.Information, Message = "Action center evaluated: {Holdings} holdings, {Items} items, {Open} open, {Unavailable} not evaluated, in {ElapsedMs} ms")]
    public static partial void Evaluated(ILogger logger, int holdings, int items, int open, int unavailable, long elapsedMs);

    [LoggerMessage(EventId = 4052, Level = LogLevel.Information, Message = "Action center decision {Decision} on a {Rule} item")]
    public static partial void Decided(ILogger logger, string decision, string rule);

    [LoggerMessage(EventId = 4053, Level = LogLevel.Information, Message = "Action center: {Cleared} done decisions cleared because their condition no longer holds ({Source})")]
    public static partial void Cleared(ILogger logger, int cleared, string source);

    [LoggerMessage(EventId = 4054, Level = LogLevel.Warning, Message = "Action center: the done decisions could not be reconciled ({Source}); nothing was changed")]
    public static partial void ReconcileFailed(ILogger logger, string source, Exception exception);

    [LoggerMessage(EventId = 4056, Level = LogLevel.Warning, Message = "Portfolio notices: the open Action-center items could not be counted; the alert notices are still returned")]
    public static partial void NoticeCountFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4055, Level = LogLevel.Information, Message = "Action center thresholds changed: {Changed} values")]
    public static partial void ThresholdsChanged(ILogger logger, int changed);
}
