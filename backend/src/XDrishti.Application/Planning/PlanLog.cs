using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Planning;

/// <summary>Structured log events of the plan record (P7-03; ids 4650 to 4654). Ids, state codes, counts and error codes only: never a reason, a modification or an account.</summary>
internal static partial class PlanLog
{
    [LoggerMessage(EventId = 4650, Level = LogLevel.Information, Message = "plans.ticket.moved {ItemId}: {From} to {To}")]
    public static partial void TicketMoved(ILogger logger, long itemId, string from, string to);

    [LoggerMessage(EventId = 4651, Level = LogLevel.Warning, Message = "plans.move.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 4652, Level = LogLevel.Information, Message = "plans.plan.moved {PlanId}: {From} to {To}")]
    public static partial void PlanMoved(ILogger logger, long planId, string from, string to);

    [LoggerMessage(EventId = 4653, Level = LogLevel.Information, Message = "plans.cutoff.applied {PlanId}: {Lapsed} tickets lapsed, plan {To}")]
    public static partial void CutoffApplied(ILogger logger, long planId, int lapsed, string to);
}
