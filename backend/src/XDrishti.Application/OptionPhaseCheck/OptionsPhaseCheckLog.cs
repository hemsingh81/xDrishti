using Microsoft.Extensions.Logging;

namespace XDrishti.Application.OptionPhaseCheck;

/// <summary>Structured log events of the P14 phase check (P14-17; ids 7080 to 7081). Counts and words only.</summary>
internal static partial class OptionsPhaseCheckLog
{
    [LoggerMessage(EventId = 7080, Level = LogLevel.Information, Message = "options.phase_check.evaluated: {Verdict}, open {Open}, violations {Violations}, cut {Cut}")]
    public static partial void Evaluated(ILogger logger, string verdict, int open, int violations, int cut);

    [LoggerMessage(EventId = 7081, Level = LogLevel.Warning, Message = "options.phase_check.unreadable {Source}: {Kind}")]
    public static partial void Unreadable(ILogger logger, string source, string kind);
}
