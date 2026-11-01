using Microsoft.Extensions.Logging;

namespace XDrishti.Application.OptionQuality;

/// <summary>Structured log events of the option-archive quality run (event ids 6971 to 6980; P14-04): <c>options.quality.run|session|done</c>. Counts only: no symbol, price, IV, Greek or id of a contract.</summary>
internal static partial class OptionQualityLog
{
    [LoggerMessage(EventId = 6971, Level = LogLevel.Information, Message = "options.quality.run trigger={Trigger} chain_pending={ChainPending} rolling_pending={RollingPending}")]
    public static partial void Run(ILogger logger, string trigger, int chainPending, int rollingPending);

    [LoggerMessage(EventId = 6972, Level = LogLevel.Information, Message = "options.quality.session source={Source} underlyings={Underlyings} results={Results}")]
    public static partial void Session(ILogger logger, string source, int underlyings, int results);

    [LoggerMessage(EventId = 6973, Level = LogLevel.Information, Message = "options.quality.done sessions={Sessions} results={Results} stopped={Stopped}")]
    public static partial void Done(ILogger logger, int sessions, int results, bool stopped);

    [LoggerMessage(EventId = 6974, Level = LogLevel.Warning, Message = "options.quality.failed unit={Unit} error={Error}: left for the next run")]
    public static partial void Failed(ILogger logger, string unit, string error);
}
