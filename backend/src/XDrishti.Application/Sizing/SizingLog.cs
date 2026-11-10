using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Sizing;

/// <summary>Structured log events of the size scaling (BK-10; ids 7380 to 7389). Counts and codes only: never an amount of capital, a password or an actor's secret.</summary>
internal static partial class SizingLog
{
    [LoggerMessage(EventId = 7380, Level = LogLevel.Information, Message = "sizing.scaled date {Date}: {Tickets} tickets, {Scaled} scaled down, {Capped} capacity capped, {Reserved} sent to the reserve")]
    public static partial void Scaled(ILogger logger, DateOnly date, int tickets, int scaled, int capped, int reserved);

    [LoggerMessage(EventId = 7381, Level = LogLevel.Information, Message = "sizing.settings_changed {Rows} rows, {Loosened} loosening, version {Version}")]
    public static partial void SettingsChanged(ILogger logger, int rows, int loosened, long version);

    [LoggerMessage(EventId = 7382, Level = LogLevel.Information, Message = "sizing.budget_changed direction {Direction}")]
    public static partial void BudgetChanged(ILogger logger, string direction);

    [LoggerMessage(EventId = 7383, Level = LogLevel.Warning, Message = "sizing.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 7384, Level = LogLevel.Information, Message = "sizing.study {Trades} trades, {Instruments} instruments")]
    public static partial void Study(ILogger logger, int trades, int instruments);
}
