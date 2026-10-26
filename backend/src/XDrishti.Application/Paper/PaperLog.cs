using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Paper;

/// <summary>Structured log events of the paper capture (P10-01; ids 5200 to 5209; P10-02 uses 5210 to 5219). Counts, ids and codes only: no price, no symbol, no account.</summary>
internal static partial class PaperLog
{
    [LoggerMessage(EventId = 5200, Level = LogLevel.Information, Message = "paper.session {Session}: {Seen} seen, {Entries} entries, {Exits} exits, {Skips} skips, {Pending} pending, {Integrity} integrity")]
    public static partial void Session(ILogger logger, DateOnly session, int seen, int entries, int exits, int skips, int pending, int integrity);

    [LoggerMessage(EventId = 5201, Level = LogLevel.Information, Message = "paper.done: {Sessions} session(s), {Entries} entries, {Exits} exits, {Skips} skips, {Pending} pending, {Failed} failed")]
    public static partial void Done(ILogger logger, int sessions, int entries, int exits, int skips, int pending, int failed);

    [LoggerMessage(EventId = 5202, Level = LogLevel.Error, Message = "paper.sheet_missing: the configured charge sheet {SheetId} is not stored; nothing captured")]
    public static partial void SheetMissing(ILogger logger, string sheetId);

    [LoggerMessage(EventId = 5203, Level = LogLevel.Information, Message = "paper.pending plan {PlanId} item {ItemId}: {ReasonCode}")]
    public static partial void Pending(ILogger logger, long planId, long itemId, string reasonCode);

    [LoggerMessage(EventId = 5204, Level = LogLevel.Error, Message = "paper.integrity plan {PlanId} item {ItemId}: a later replay shows a different entry than the stored one; nothing was overwritten")]
    public static partial void Integrity(ILogger logger, long planId, long itemId);

    [LoggerMessage(EventId = 5205, Level = LogLevel.Error, Message = "paper.session_failed {Session}: {Reason}")]
    public static partial void SessionFailed(ILogger logger, DateOnly session, string reason);

    [LoggerMessage(EventId = 5210, Level = LogLevel.Information, Message = "paper.ledger read: {Accounts} account(s), {Open} open, {Closed} closed")]
    public static partial void LedgerRead(ILogger logger, int accounts, int open, int closed);

    [LoggerMessage(EventId = 5211, Level = LogLevel.Warning, Message = "paper.book unavailable for the plan: {Code}")]
    public static partial void BookUnavailable(ILogger logger, string code);

    [LoggerMessage(EventId = 5212, Level = LogLevel.Information, Message = "paper.book read for the plan: {Accounts} account(s), {Positions} position(s), {Taken} taken trade(s)")]
    public static partial void BookRead(ILogger logger, int accounts, int positions, int taken);

    [LoggerMessage(EventId = 5213, Level = LogLevel.Warning, Message = "paper.reconcile found {Differences} difference(s)")]
    public static partial void ReconcileDifferences(ILogger logger, int differences);

    [LoggerMessage(EventId = 5214, Level = LogLevel.Information, Message = "paper.review {Week} epoch {EpochId} built as a dry run")]
    public static partial void ReviewDryRun(ILogger logger, string week, long epochId);

    [LoggerMessage(EventId = 5215, Level = LogLevel.Information, Message = "paper.review {Week} epoch {EpochId} stored as {Id} ({Sections} sections)")]
    public static partial void ReviewStored(ILogger logger, string week, long epochId, long id, int sections);

    [LoggerMessage(EventId = 5216, Level = LogLevel.Information, Message = "paper.review {Week} epoch {EpochId} was stored already as {Id}; nothing written")]
    public static partial void ReviewRepeat(ILogger logger, string week, long epochId, long id);

    [LoggerMessage(EventId = 5217, Level = LogLevel.Warning, Message = "paper.review source {Source} unavailable: {Reason}")]
    public static partial void ReviewSourceUnavailable(ILogger logger, string source, string reason);

    [LoggerMessage(EventId = 5218, Level = LogLevel.Information, Message = "paper.discipline read: {Sessions} session(s), {Tickets} primary ticket(s)")]
    public static partial void DisciplineRead(ILogger logger, int sessions, int tickets);
}
