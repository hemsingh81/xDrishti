using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Filings;

/// <summary>
/// Structured log events of the filings ingest (event ids 6986 to 6995, P15-02): counts, ids and closed reason codes only. Never a file's bytes, a path, a file name or a source reference.
/// </summary>
internal static partial class FilingsIngestLog
{
    [LoggerMessage(EventId = 6986, Level = LogLevel.Information, Message = "filings.ingested stored={Stored} revisions={Revisions} out_of_order={OutOfOrder} trigger={Trigger}")]
    public static partial void Ingested(ILogger logger, int stored, int revisions, int outOfOrder, string trigger);

    [LoggerMessage(EventId = 6987, Level = LogLevel.Information, Message = "filings.duplicate count={Count}")]
    public static partial void Duplicate(ILogger logger, int count);

    [LoggerMessage(EventId = 6988, Level = LogLevel.Warning, Message = "filings.sidecar_conflict stored_id={StoredId} stored_instrument={StoredInstrument} stored_kind={StoredKind} stored_period={StoredPeriod} stored_broadcast={StoredBroadcast} new_instrument={NewInstrument} new_kind={NewKind} new_period={NewPeriod} new_broadcast={NewBroadcast}: nothing changed")]
    public static partial void SidecarConflict(ILogger logger, Guid storedId, Guid storedInstrument, string storedKind, string storedPeriod, string storedBroadcast, Guid newInstrument, string newKind, string newPeriod, string newBroadcast);

    [LoggerMessage(EventId = 6989, Level = LogLevel.Information, Message = "filings.unavailable count={Count} reasons={Reasons}")]
    public static partial void Unavailable(ILogger logger, int count, string reasons);

    [LoggerMessage(EventId = 6990, Level = LogLevel.Information, Message = "filings.rejected count={Count} reasons={Reasons}")]
    public static partial void Rejected(ILogger logger, int count, string reasons);

    [LoggerMessage(EventId = 6991, Level = LogLevel.Warning, Message = "filings.disk_stop reason={Reason} used_bytes={UsedBytes}: the run stops until the owner clears it; nothing is deleted")]
    public static partial void DiskStop(ILogger logger, string reason, long usedBytes);

    [LoggerMessage(EventId = 6992, Level = LogLevel.Warning, Message = "filings.disk_warning used_bytes={UsedBytes} max_bytes={MaxBytes}: the store folder is at 80 percent of its cap")]
    public static partial void DiskWarning(ILogger logger, long usedBytes, long maxBytes);

    [LoggerMessage(EventId = 6993, Level = LogLevel.Warning, Message = "filings.failed error={Error}: the file is left for the next run")]
    public static partial void Failed(ILogger logger, string error);

    [LoggerMessage(EventId = 6994, Level = LogLevel.Information, Message = "filings.run {Counts}")]
    public static partial void Run(ILogger logger, string counts);
}
