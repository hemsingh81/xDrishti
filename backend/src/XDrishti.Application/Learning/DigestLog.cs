using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning;

/// <summary>Structured log events of the weekly digest (P7-15; ids 4760 to 4769). Week keys and counts only.</summary>
internal static partial class DigestLog
{
    [LoggerMessage(EventId = 4760, Level = LogLevel.Information, Message = "digest.stored {IsoWeek}: id {Id}, {Sections} sections")]
    public static partial void Stored(ILogger logger, string isoWeek, long id, int sections);

    [LoggerMessage(EventId = 4761, Level = LogLevel.Information, Message = "digest.repeat {IsoWeek}: already stored as {Id}, nothing written")]
    public static partial void Repeat(ILogger logger, string isoWeek, long id);

    [LoggerMessage(EventId = 4762, Level = LogLevel.Information, Message = "digest.dry_run {IsoWeek}: built, nothing written")]
    public static partial void DryRun(ILogger logger, string isoWeek);

    [LoggerMessage(EventId = 4763, Level = LogLevel.Warning, Message = "digest.phase_check_unavailable {IsoWeek}: {Code}")]
    public static partial void PhaseUnavailable(ILogger logger, string isoWeek, string code);
}
