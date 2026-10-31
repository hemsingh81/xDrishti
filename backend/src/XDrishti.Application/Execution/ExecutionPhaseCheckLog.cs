using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Execution;

/// <summary>Structured log events of the P12 phase check (P12-12; ids 6750 to 6759). A date, a verdict word and counts only: no account name, symbol or price.</summary>
internal static partial class ExecutionPhaseCheckLog
{
    [LoggerMessage(EventId = 6750, Level = LogLevel.Information, Message = "execution.phase_check.evaluated through {Through}: exit {Exit}, accounts {Accounts}, met {Met}, not met {NotMet}")]
    public static partial void Evaluated(ILogger logger, DateOnly through, string exit, int accounts, int met, int notMet);

    [LoggerMessage(EventId = 6751, Level = LogLevel.Information, Message = "execution.phase_check.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);
}
