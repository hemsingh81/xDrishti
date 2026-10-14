using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Portfolio.BrokerHistory;

/// <summary>Structured log events of the broker history sync (event ids 4201 to 4215; 4211 is the worker's). Account slugs, run ids, counts and outcome codes only — never tokens, URLs, Dhan's raw answers, symbols, quantities, prices or ledger texts.</summary>
internal static partial class BrokerHistoryLog
{
    [LoggerMessage(EventId = 4201, Level = LogLevel.Information, Message = "Broker history {Kind} run {RunId} of account {Slug} started ({Trigger}): {Windows} windows")]
    public static partial void Started(ILogger logger, string kind, Guid runId, string slug, string trigger, int windows);

    [LoggerMessage(EventId = 4202, Level = LogLevel.Information, Message = "Broker history {Kind} run {RunId} done: {Stored} new, {Duplicates} already stored, {Dropped} unusable, {Skipped} skipped in {Windows} windows ({Halved} halved)")]
    public static partial void Succeeded(ILogger logger, string kind, Guid runId, int stored, int duplicates, int dropped, int skipped, int windows, int halved);

    [LoggerMessage(EventId = 4203, Level = LogLevel.Warning, Message = "Broker history {Kind} run {RunId} failed: {Reason}")]
    public static partial void Failed(ILogger logger, string kind, Guid runId, string reason);

    [LoggerMessage(EventId = 4204, Level = LogLevel.Error, Message = "Broker history {Kind} run {RunId} failed unexpectedly")]
    public static partial void Crashed(ILogger logger, string kind, Guid runId, Exception exception);

    [LoggerMessage(EventId = 4205, Level = LogLevel.Information, Message = "Broker history window {Window} of {Total} of run {RunId} was too large and was halved")]
    public static partial void Halved(ILogger logger, int window, int total, Guid runId);

    [LoggerMessage(EventId = 4206, Level = LogLevel.Information, Message = "Automatic rebuild after broker trades of account {Slug}: {Rebuilt} rebuilt, {Unchanged} unchanged, {NeedsReview} need review, {NotRebuilt} left alone")]
    public static partial void Rebuilt(ILogger logger, string slug, int rebuilt, int unchanged, int needsReview, int notRebuilt);

    [LoggerMessage(EventId = 4207, Level = LogLevel.Information, Message = "Automatic rebuild of holding {HoldingId} skipped: it changed meanwhile")]
    public static partial void RebuildRaced(ILogger logger, Guid holdingId);

    [LoggerMessage(EventId = 4208, Level = LogLevel.Information, Message = "Scheduled broker history: {Done} of {Accounts} accounts read, {Failed} failed")]
    public static partial void ScheduledDone(ILogger logger, int done, int accounts, int failed);

    [LoggerMessage(EventId = 4209, Level = LogLevel.Information, Message = "Scheduled broker history of account {Slug} skipped: {Reason}")]
    public static partial void ScheduledSkipped(ILogger logger, string slug, string reason);

    [LoggerMessage(EventId = 4210, Level = LogLevel.Error, Message = "Broker history run {RunId} could not be ended as failed; the cleanup of interrupted syncs will end it")]
    public static partial void AbandonFailed(ILogger logger, Guid runId, Exception exception);
}
