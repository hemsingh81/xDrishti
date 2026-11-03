using Microsoft.Extensions.Logging;

namespace XDrishti.Application.LearningPhase;

/// <summary>Structured log events of the P15 phase check (P15-12; ids 7040 to 7041). Counts and words only.</summary>
internal static partial class EventsPhaseCheckLog
{
    [LoggerMessage(EventId = 7040, Level = LogLevel.Information, Message = "events.phase_check.evaluated: {Verdict}, open families {Open}, violations {Violations}, cut {Cut}")]
    public static partial void Evaluated(ILogger logger, string verdict, int open, int violations, int cut);

    [LoggerMessage(EventId = 7041, Level = LogLevel.Warning, Message = "events.phase_check.unreadable {Source}: {Kind}")]
    public static partial void Unreadable(ILogger logger, string source, string kind);
}
