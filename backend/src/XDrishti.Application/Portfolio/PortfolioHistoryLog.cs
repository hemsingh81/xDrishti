using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Portfolio;

/// <summary>Structured log events of the portfolio history. Only sizes and durations are logged — never figures, symbols or holding details.</summary>
internal static partial class PortfolioHistoryLog
{
    [LoggerMessage(EventId = 4031, Level = LogLevel.Information, Message = "Portfolio history computed: {Holdings} holdings, {Instruments} instruments, {Days} days, {Points} points in {ElapsedMs} ms")]
    public static partial void Computed(ILogger logger, int holdings, int instruments, int days, int points, long elapsedMs);

    [LoggerMessage(EventId = 4032, Level = LogLevel.Information, Message = "Portfolio history served from the cache: {Points} points")]
    public static partial void Cached(ILogger logger, int points);

    [LoggerMessage(EventId = 4033, Level = LogLevel.Information, Message = "Portfolio benchmark changed")]
    public static partial void BenchmarkChanged(ILogger logger);

    [LoggerMessage(EventId = 4034, Level = LogLevel.Information, Message = "Portfolio holdings tracked: {Requested} requested, {Added} added, {AlreadyTracked} already tracked")]
    public static partial void HoldingsTracked(ILogger logger, int requested, int added, int alreadyTracked);

    [LoggerMessage(EventId = 4035, Level = LogLevel.Information, Message = "Portfolio benchmark tracked: {Added} added")]
    public static partial void BenchmarkTracked(ILogger logger, int added);
}
