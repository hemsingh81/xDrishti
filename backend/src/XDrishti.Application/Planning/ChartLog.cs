using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Planning;

/// <summary>Structured log events of the ticket chart (P8-14; id 4795). Ids and counts only.</summary>
internal static partial class ChartLog
{
    [LoggerMessage(EventId = 4795, Level = LogLevel.Information, Message = "plans.chart: plan {PlanId} item {ItemId}, {Bars} bar(s), {Ms} ms")]
    public static partial void Read(ILogger logger, long planId, long itemId, int bars, long ms);
}
