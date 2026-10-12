using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Market.Bars;

/// <summary>Structured log events of the market data pipeline (event ids 3301 to 3399: unique across the solution, asserted by the architecture tests). Counts, phases and file names only: never tokens, URLs or the broker's answers.</summary>
internal static partial class DataLog
{
    [LoggerMessage(EventId = 3301, Level = LogLevel.Information, Message = "Data job {JobId} started by the owner: steps {Steps}, force CSV {ForceCsv}")]
    public static partial void JobStarted(ILogger logger, Guid jobId, string steps, bool forceCsv);

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
}
