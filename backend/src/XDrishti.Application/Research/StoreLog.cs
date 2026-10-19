using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Research;

/// <summary>
/// Structured log events of the result store (spec P3-10, D15; event ids 4415 to 4418: unique across the solution, asserted by the architecture tests). The run key (first 12 characters), counts, figure names, codes and milliseconds only:
/// never a trade, the record text, the strategy definition or a sheet.
/// </summary>
internal static partial class StoreLog
{
    [LoggerMessage(EventId = 4415, Level = LogLevel.Information, Message = "store.started run {RunKey}: {Trades} trades, {OpenAtEnd} open at end")]
    public static partial void Started(ILogger logger, string runKey, int trades, int openAtEnd);

    [LoggerMessage(EventId = 4416, Level = LogLevel.Information, Message = "store.completed run {RunKey}: {Trades} trades, {OpenAtEnd} open at end, {Outcome}, copy {CopyMs} ms, total {TotalMs} ms")]
    public static partial void Completed(ILogger logger, string runKey, int trades, int openAtEnd, string outcome, long copyMs, long totalMs);

    [LoggerMessage(EventId = 4417, Level = LogLevel.Error, Message = "store.conflict run {RunKey}: another record is stored under this key; differing figures: {Differing}")]
    public static partial void Conflict(ILogger logger, string runKey, string differing);

    [LoggerMessage(EventId = 4418, Level = LogLevel.Error, Message = "store.failed run {RunKey}: {Code}")]
    public static partial void Failed(ILogger logger, string runKey, string code);
}
