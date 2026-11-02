using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning;

/// <summary>Structured log events of the extension extraction (P15-05; ids 4490 and 4491). Keys (first 12 characters), versions, counts and milliseconds only: never a price, a fact or a trade.</summary>
internal static partial class ExtensionFeatureLog
{
    [LoggerMessage(EventId = 4490, Level = LogLevel.Information, Message = "learning.extension.completed run {RunKey} {Version}: {Trades} trades, {Stored} new rows, {Unknown} unknown values, total {TotalMs} ms")]
    public static partial void Extracted(ILogger logger, string runKey, string version, int trades, int stored, int unknown, long totalMs);

    [LoggerMessage(EventId = 4491, Level = LogLevel.Warning, Message = "learning.extension.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);
}
