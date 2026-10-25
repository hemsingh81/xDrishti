using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Paper;

/// <summary>Structured log events of the paper capture (P10-01; ids 5200 to 5209). Counts, ids and codes only: no price, no symbol, no account.</summary>
internal static partial class PaperLog
{
    [LoggerMessage(EventId = 5200, Level = LogLevel.Information, Message = "paper.session {Session}: {Seen} seen, {Entries} entries, {Exits} exits, {Skips} skips, {Pending} pending, {Integrity} integrity")]
    public static partial void Session(ILogger logger, DateOnly session, int seen, int entries, int exits, int skips, int pending, int integrity);

    [LoggerMessage(EventId = 5201, Level = LogLevel.Information, Message = "paper.done: {Sessions} session(s), {Entries} entries, {Exits} exits, {Skips} skips, {Pending} pending, {Failed} failed")]
    public static partial void Done(ILogger logger, int sessions, int entries, int exits, int skips, int pending, int failed);

    [LoggerMessage(EventId = 5202, Level = LogLevel.Error, Message = "paper.sheet_missing: the configured charge sheet {SheetId} is not stored; nothing captured")]
    public static partial void SheetMissing(ILogger logger, string sheetId);

    [LoggerMessage(EventId = 5203, Level = LogLevel.Information, Message = "paper.pending plan {PlanId} item {ItemId}: {Reason}")]
    public static partial void Pending(ILogger logger, long planId, long itemId, string reason);

    [LoggerMessage(EventId = 5204, Level = LogLevel.Error, Message = "paper.integrity plan {PlanId} item {ItemId}: a later replay shows a different entry than the stored one; nothing was overwritten")]
    public static partial void Integrity(ILogger logger, long planId, long itemId);

    [LoggerMessage(EventId = 5205, Level = LogLevel.Error, Message = "paper.session_failed {Session}: {Reason}")]
    public static partial void SessionFailed(ILogger logger, DateOnly session, string reason);
}
