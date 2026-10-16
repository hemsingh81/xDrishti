using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Portfolio.Reports;

/// <summary>Structured log events of the report import (event ids 4191 to 4199). Account slugs, kinds, counts and durations only — never file names, scrip names, client codes, quantities or amounts.</summary>
internal static partial class ReportsLog
{
    [LoggerMessage(EventId = 4191, Level = LogLevel.Information, Message = "Report previewed for account {Slug}: kind {Kind}, {Rows} rows, {ToAdd} to add, in {ElapsedMs} ms")]
    public static partial void Previewed(ILogger logger, string slug, string kind, int rows, int toAdd, long elapsedMs);

    [LoggerMessage(EventId = 4192, Level = LogLevel.Information, Message = "Report imported for account {Slug}: kind {Kind}, {Stored} stored, in {ElapsedMs} ms")]
    public static partial void Imported(ILogger logger, string slug, string kind, int stored, long elapsedMs);

    [LoggerMessage(EventId = 4193, Level = LogLevel.Information, Message = "Cost import {BatchId} of account {Slug} undone: {Voided} entries voided")]
    public static partial void CostsUndone(ILogger logger, Guid batchId, string slug, int voided);
}
