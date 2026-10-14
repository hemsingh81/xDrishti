using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>Structured log events of the dividend file import (event ids 4181 to 4189). Account slugs, ids, counts and durations only — never file names, scrip names or amounts.</summary>
internal static partial class DividendImportLog
{
    [LoggerMessage(EventId = 4181, Level = LogLevel.Information, Message = "Dividend file previewed for account {Slug}: {Rows} rows, {Parsed} valid, {Bad} bad, {ToAdd} to add, in {ElapsedMs} ms")]
    public static partial void Previewed(ILogger logger, string slug, int rows, int parsed, int bad, int toAdd, long elapsedMs);

    [LoggerMessage(EventId = 4182, Level = LogLevel.Information, Message = "Dividends imported for account {Slug}: {Imported} stored, {Skipped} not stored, {Aliases} names remembered, in {ElapsedMs} ms")]
    public static partial void Imported(ILogger logger, string slug, int imported, int skipped, int aliases, long elapsedMs);

    [LoggerMessage(EventId = 4183, Level = LogLevel.Information, Message = "Dividend import {BatchId} of account {Slug} undone: {Voided} dividends voided")]
    public static partial void Undone(ILogger logger, Guid batchId, string slug, int voided);
}
