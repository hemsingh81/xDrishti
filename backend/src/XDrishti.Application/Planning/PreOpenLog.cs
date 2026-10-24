using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Planning;

/// <summary>Structured log events of the pre-open check, arming and expiry (P7-11; ids 4780 to 4789). Ids, counts and codes only: never a price, a symbol, an account or a reason text.</summary>
internal static partial class PreOpenLog
{
    [LoggerMessage(EventId = 4780, Level = LogLevel.Information, Message = "plans.preopen.checked {PlanId}: {Armed} armed, {Invalidated} invalidated")]
    public static partial void Checked(ILogger logger, long planId, int armed, int invalidated);

    [LoggerMessage(EventId = 4781, Level = LogLevel.Information, Message = "plans.preopen.invalidated {ItemId}: {Code}")]
    public static partial void Invalidated(ILogger logger, long itemId, string code);

    [LoggerMessage(EventId = 4782, Level = LogLevel.Warning, Message = "plans.preopen.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 4783, Level = LogLevel.Information, Message = "plans.expiry.done {PlanId}: {Tickets} tickets expired, plan {To}")]
    public static partial void Expired(ILogger logger, long planId, int tickets, string to);

    [LoggerMessage(EventId = 4784, Level = LogLevel.Warning, Message = "plans.preopen.source_problem")]
    public static partial void SourceProblem(ILogger logger);

    [LoggerMessage(EventId = 4785, Level = LogLevel.Warning, Message = "plans.armed.not_quotable {ItemId}: no Dhan NSE equity id for the symbol")]
    public static partial void NotQuotable(ILogger logger, long itemId);

    [LoggerMessage(EventId = 4786, Level = LogLevel.Warning, Message = "plans.armed.provider_failed {Failure}: armed tickets left out of the active set this time")]
    public static partial void ProviderFailed(ILogger logger, string failure);

    [LoggerMessage(EventId = 4787, Level = LogLevel.Warning, Message = "plans.preopen.move_failed {ItemId}: {Code}")]
    public static partial void MoveFailed(ILogger logger, long itemId, string code);
}
