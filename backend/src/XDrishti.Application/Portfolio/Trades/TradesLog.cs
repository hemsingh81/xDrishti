using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Portfolio.Trades;

/// <summary>Structured log events of the trade import (event ids 4071 to 4079). Account slugs, ids, counts and durations only — never file names, symbols, quantities or prices.</summary>
internal static partial class TradesLog
{
    [LoggerMessage(EventId = 4071, Level = LogLevel.Information, Message = "Trade file previewed for account {Slug}: {Rows} rows, {Parsed} valid, {Skipped} skipped, {Bad} bad, {Securities} securities, in {ElapsedMs} ms")]
    public static partial void Previewed(ILogger logger, string slug, int rows, int parsed, int skipped, int bad, int securities, long elapsedMs);

    [LoggerMessage(EventId = 4072, Level = LogLevel.Information, Message = "Trades imported for account {Slug}: {Imported} stored, {Duplicates} already imported or repeated, {Rebuilt} holdings rebuilt, in {ElapsedMs} ms")]
    public static partial void Imported(ILogger logger, string slug, int imported, int duplicates, int rebuilt, long elapsedMs);

    [LoggerMessage(EventId = 4073, Level = LogLevel.Information, Message = "Trade import {BatchId} of account {Slug} undone: {Voided} trades voided, {Rebuilt} holdings rebuilt")]
    public static partial void Undone(ILogger logger, Guid batchId, string slug, int voided, int rebuilt);

    [LoggerMessage(EventId = 4074, Level = LogLevel.Information, Message = "Trade {TradeId} of account {Slug} added by hand")]
    public static partial void Added(ILogger logger, Guid tradeId, string slug);

    [LoggerMessage(EventId = 4075, Level = LogLevel.Information, Message = "Trade {TradeId} of account {Slug} voided")]
    public static partial void Voided(ILogger logger, Guid tradeId, string slug);

    [LoggerMessage(EventId = 4076, Level = LogLevel.Information, Message = "Lots of holding {HoldingId} rebuilt from trades")]
    public static partial void Rebuilt(ILogger logger, Guid holdingId);
}
