using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Reporting;

/// <summary>Structured log events of the report endpoints (P8-06; event ids 4800 to 4802: unique across the solution, asserted by the architecture tests). The kind of report, counts, a code and milliseconds only: never a trade, a symbol, a strategy or a figure.</summary>
internal static partial class ReportLog
{
    [LoggerMessage(EventId = 4800, Level = LogLevel.Information, Message = "reports.loaded kind {Kind}: {Runs} runs, {Trades} trades, basis {Basis}, {Milliseconds} ms")]
    public static partial void Loaded(ILogger logger, string kind, int runs, int trades, string basis, long milliseconds);

    [LoggerMessage(EventId = 4802, Level = LogLevel.Information, Message = "report.benchmark: {Baskets} baskets, {Instruments} instruments read, {Excluded} excluded, {Milliseconds} ms")]
    public static partial void Benchmark(ILogger logger, int baskets, int instruments, int excluded, long milliseconds);

    [LoggerMessage(EventId = 4801, Level = LogLevel.Information, Message = "reports.refused kind {Kind}: {Code}")]
    public static partial void Refused(ILogger logger, string kind, string code);

    [LoggerMessage(EventId = 4803, Level = LogLevel.Information, Message = "report.read kind {Kind}: {Plans} plans, {Lines} lines, {Milliseconds} ms")]
    public static partial void Read(ILogger logger, string kind, int plans, int lines, long milliseconds);

    [LoggerMessage(EventId = 4897, Level = LogLevel.Information, Message = "report.replay run {RunPrefix} ordinal {Ordinal} timeframe {Timeframe}: {Bars} bars, {Milliseconds} ms")]
    public static partial void Replay(ILogger logger, string runPrefix, int ordinal, string timeframe, int bars, long milliseconds);
}
