using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Market.Bars;

/// <summary>Structured log events of the market data pipeline (event ids 3301 to 3399: unique across the solution, asserted by the architecture tests). Counts, phases and file names only: never tokens, URLs or the broker's answers.</summary>
internal static partial class DataLog
{
    [LoggerMessage(EventId = 3301, Level = LogLevel.Information, Message = "Data job {JobId} started ({Trigger}): steps {Steps}, force CSV {ForceCsv}")]
    public static partial void JobStarted(ILogger logger, Guid jobId, string steps, bool forceCsv, string trigger);

    [LoggerMessage(EventId = 3302, Level = LogLevel.Information, Message = "Data job {JobId} {Status}")]
    public static partial void JobFinished(ILogger logger, Guid jobId, string status);

    [LoggerMessage(EventId = 3303, Level = LogLevel.Error, Message = "Data job {JobId}: the {Phase} phase failed unexpectedly")]
    public static partial void PhaseCrashed(ILogger logger, Guid jobId, string phase, Exception exception);

    [LoggerMessage(EventId = 3304, Level = LogLevel.Warning, Message = "Data job {JobId} was interrupted because the application is stopping")]
    public static partial void JobCancelled(ILogger logger, Guid jobId);

    [LoggerMessage(EventId = 3305, Level = LogLevel.Warning, Message = "{Count} data job(s) that were running when the application stopped were marked failed")]
    public static partial void InterruptedJobsRecovered(ILogger logger, int count);

    [LoggerMessage(EventId = 3306, Level = LogLevel.Information, Message = "CSV file {File}: {Rows} rows, {Inserted} inserted, {Updated} updated, {Unchanged} unchanged, {Repaired} repaired, {Rejected} rejected")]
    public static partial void CsvFileLoaded(ILogger logger, string file, long rows, long inserted, long updated, long unchanged, long repaired, long rejected);

    [LoggerMessage(EventId = 3307, Level = LogLevel.Warning, Message = "CSV file {File} could not be loaded: {Reason}")]
    public static partial void CsvFileFailed(ILogger logger, string file, string reason);

    [LoggerMessage(EventId = 3308, Level = LogLevel.Error, Message = "CSV file {File} could not be stored")]
    public static partial void CsvFileCrashed(ILogger logger, string file, Exception exception);

    [LoggerMessage(EventId = 3309, Level = LogLevel.Warning, Message = "Data job {JobId}: loaded instruments could not be tracked: {Reason}")]
    public static partial void TrackingFailed(ILogger logger, Guid jobId, string reason);

    [LoggerMessage(EventId = 3310, Level = LogLevel.Information, Message = "Dhan fetch for {Symbol}: {Bars} bars, {Inserted} new, {Updated} changed, {Rejected} rejected")]
    public static partial void DhanInstrumentFetched(ILogger logger, string symbol, long bars, long inserted, long updated, long rejected);

    [LoggerMessage(EventId = 3311, Level = LogLevel.Warning, Message = "Dhan fetch for {Symbol} failed: {Reason}")]
    public static partial void DhanInstrumentFailed(ILogger logger, string symbol, string reason);

    [LoggerMessage(EventId = 3312, Level = LogLevel.Warning, Message = "Dhan fetch for {Symbol}: {Mismatched} of {Compared} overlapping bars differ from the stored ones by more than 0.05 %")]
    public static partial void DhanOverlapMismatch(ILogger logger, string symbol, long mismatched, long compared);

    [LoggerMessage(EventId = 3313, Level = LogLevel.Error, Message = "Dhan fetch for {Symbol} could not be stored")]
    public static partial void DhanInstrumentCrashed(ILogger logger, string symbol, Exception exception);

    [LoggerMessage(EventId = 3314, Level = LogLevel.Information, Message = "Aggregates of {Symbol}: {Buckets} bars written")]
    public static partial void AggregatesBuilt(ILogger logger, string symbol, long buckets);

    [LoggerMessage(EventId = 3315, Level = LogLevel.Warning, Message = "Data job {JobId} was ended elsewhere (marked failed as interrupted) while its worker was still running; the worker stops reporting")]
    public static partial void JobEndedElsewhere(ILogger logger, Guid jobId);

    [LoggerMessage(EventId = 3316, Level = LogLevel.Error, Message = "Dhan fetch for {Symbol}: missing bars could not be looked for")]
    public static partial void GapPlanFailed(ILogger logger, string symbol, Exception exception);

    [LoggerMessage(EventId = 3317, Level = LogLevel.Error, Message = "Dhan fetch for {Symbol}: the gap records could not be updated")]
    public static partial void GapCompleteFailed(ILogger logger, string symbol, Exception exception);

    [LoggerMessage(EventId = 3318, Level = LogLevel.Information, Message = "The end-of-day update is switched off (Data:Eod:Enabled)")]
    public static partial void EodOff(ILogger logger);

    [LoggerMessage(EventId = 3319, Level = LogLevel.Information, Message = "The next end-of-day update is at {Next:yyyy-MM-dd HH:mm} India time")]
    public static partial void EodScheduled(ILogger logger, DateTimeOffset next);

    [LoggerMessage(EventId = 3320, Level = LogLevel.Information, Message = "The end-of-day update of today was missed (the application was not running): updating now")]
    public static partial void EodCatchUp(ILogger logger);

    [LoggerMessage(EventId = 3321, Level = LogLevel.Information, Message = "Scheduled end-of-day data job {JobId} started")]
    public static partial void EodStarted(ILogger logger, Guid jobId);

    [LoggerMessage(EventId = 3322, Level = LogLevel.Warning, Message = "The scheduled end-of-day update was skipped: {Reason}")]
    public static partial void EodSkipped(ILogger logger, string reason);

    [LoggerMessage(EventId = 3323, Level = LogLevel.Error, Message = "The scheduled end-of-day update could not be started")]
    public static partial void EodCrashed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3390, Level = LogLevel.Warning, Message = "The cleanup of interrupted data jobs did not finish in time; the end-of-day catch-up goes on without it")]
    public static partial void EodRecoveryTimedOut(ILogger logger);

    [LoggerMessage(EventId = 3324, Level = LogLevel.Information, Message = "CSV load of {File} undone: {Bars} bars removed")]
    public static partial void CsvLoadUndone(ILogger logger, string file, long bars);

    [LoggerMessage(EventId = 3325, Level = LogLevel.Error, Message = "The undo of a CSV load could not be written to the activity log")]
    public static partial void CsvUndoAuditFailed(ILogger logger, Exception exception);
}
