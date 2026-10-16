using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Portfolio.Heal;

/// <summary>Structured log events of the self-healing syncs (event ids 4301 to 4315). Kinds, reason codes, account slugs, counts and dates only - never tokens, symbols, quantities, prices or ledger texts.</summary>
internal static partial class HealLog
{
    [LoggerMessage(EventId = 4301, Level = LogLevel.Information, Message = "Sync of kind {Kind} failed: {Reason}")]
    public static partial void Failed(ILogger logger, string kind, string reason);

    [LoggerMessage(EventId = 4302, Level = LogLevel.Warning, Message = "The health figures of a sync could not be written")]
    public static partial void RecordFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4303, Level = LogLevel.Information, Message = "Retrying {Kind} of account {Slug} (attempt {Attempt})")]
    public static partial void Retrying(ILogger logger, string kind, string slug, int attempt);

    [LoggerMessage(EventId = 4304, Level = LogLevel.Information, Message = "Account {Slug} was reconnected: catching up the missed {Kinds}")]
    public static partial void CatchingUp(ILogger logger, string slug, string kinds);

    [LoggerMessage(EventId = 4305, Level = LogLevel.Error, Message = "The healing of account {Slug} failed unexpectedly")]
    public static partial void AccountCrashed(ILogger logger, string slug, Exception exception);

    [LoggerMessage(EventId = 4306, Level = LogLevel.Error, Message = "The healing tick failed unexpectedly")]
    public static partial void TickCrashed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4307, Level = LogLevel.Information, Message = "Account {Slug}: {Windows} windows that dropped rows are read again because the Dhan mapping changed (version {Version})")]
    public static partial void Rereading(ILogger logger, string slug, int windows, int version);

    [LoggerMessage(EventId = 4308, Level = LogLevel.Information, Message = "Account {Slug}: earlier trade history read back to {DeepenedTo} for {Holdings} holdings the trades did not explain")]
    public static partial void Deepened(ILogger logger, string slug, DateOnly deepenedTo, int holdings);

    [LoggerMessage(EventId = 4309, Level = LogLevel.Information, Message = "Account {Slug}: the deepening of the trade history stops ({Why})")]
    public static partial void DeepenStopped(ILogger logger, string slug, string why);

    [LoggerMessage(EventId = 4310, Level = LogLevel.Information, Message = "Account {Slug}: the deepening of the trade history failed for now: {Reason}")]
    public static partial void DeepenFailed(ILogger logger, string slug, string reason);

    [LoggerMessage(EventId = 4311, Level = LogLevel.Warning, Message = "The attention items could not be worked out; the notices go without them")]
    public static partial void AttentionFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4312, Level = LogLevel.Warning, Message = "The attention items could not be brought up to date at startup; the healing tick tries again")]
    public static partial void StartupHousekeepingFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 4313, Level = LogLevel.Warning, Message = "The attention items were not reconciled at the end of a sync within {Seconds} seconds; the next healing tick does it")]
    public static partial void ReconcileTimedOut(ILogger logger, int seconds);
}
