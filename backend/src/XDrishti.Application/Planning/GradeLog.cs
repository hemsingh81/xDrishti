using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Planning;

/// <summary>Structured log events of grading (P7-13; ids 4720 to 4729). Counts and codes only: no price, no secret.</summary>
internal static partial class GradeLog
{
    [LoggerMessage(EventId = 4720, Level = LogLevel.Information, Message = "grades.done: {Plans} plan(s), {Graded} graded, {NotTriggered} not triggered, {NotGradable} not gradable, {Pending} pending")]
    public static partial void Done(ILogger logger, int plans, int graded, int notTriggered, int notGradable, int pending);

    [LoggerMessage(EventId = 4722, Level = LogLevel.Error, Message = "grades.sheet_missing: the configured charge sheet {SheetId} is not stored; nothing graded")]
    public static partial void SheetMissing(ILogger logger, string sheetId);

    [LoggerMessage(EventId = 4721, Level = LogLevel.Information, Message = "grades.pending plan {PlanId} item {ItemId}: {Reason}")]
    public static partial void Pending(ILogger logger, long planId, long itemId, string reason);
}
