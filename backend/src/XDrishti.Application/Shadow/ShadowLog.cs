using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Shadow;

/// <summary>Structured log events of the shadow record (CF-08; ids 5400 to 5409). Counts only: no level, no feature, no symbol, no strategy text.</summary>
internal static partial class ShadowLog
{
    [LoggerMessage(EventId = 5400, Level = LogLevel.Information, Message = "shadow.write: {Sessions} session(s), {Rows} row(s), {EdgeRows} counting for edge, {Gaps} gap(s), {Dropped} left out for a same trade key")]
    public static partial void Written(ILogger logger, int sessions, int rows, int edgeRows, int gaps, int dropped);

    [LoggerMessage(EventId = 5401, Level = LogLevel.Information, Message = "shadow.write: switched off, nothing written")]
    public static partial void SwitchedOff(ILogger logger);

    [LoggerMessage(EventId = 5402, Level = LogLevel.Information, Message = "shadow.grade: {Seen} looked at, {Graded} graded, {NotTriggered} not triggered, {NotGradable} not gradable, {Pending} pending, {Failed} failed")]
    public static partial void Graded(ILogger logger, int seen, int graded, int notTriggered, int notGradable, int pending, int failed);

    [LoggerMessage(EventId = 5403, Level = LogLevel.Information, Message = "shadow.grade: switched off, nothing graded")]
    public static partial void GradeSwitchedOff(ILogger logger);

    [LoggerMessage(EventId = 5404, Level = LogLevel.Information, Message = "shadow.switch: row {Row} appended, enabled {Enabled} (1 = on)")]
    public static partial void SwitchChanged(ILogger logger, int row, int enabled);

    [LoggerMessage(EventId = 5406, Level = LogLevel.Information, Message = "shadow.score: {Models} model(s) scored, {Scores} score(s), {Missing} candidate(s) without features, {ElapsedMs} ms")]
    public static partial void Scored(ILogger logger, int models, int scores, int missing, int elapsedMs);

    [LoggerMessage(EventId = 5407, Level = LogLevel.Warning, Message = "shadow.score: {Skipped} model(s) skipped, at most five are scored in a night")]
    public static partial void ModelsSkipped(ILogger logger, int skipped);

    [LoggerMessage(EventId = 5408, Level = LogLevel.Warning, Message = "shadow.score: scoring failed, the session is written without scores")]
    public static partial void ScoringFailed(ILogger logger);

    [LoggerMessage(EventId = 5409, Level = LogLevel.Warning, Message = "shadow.score: later than {Minutes} minute(s) before the cut-off, the session is written without scores")]
    public static partial void ScoringTooLate(ILogger logger, int minutes);

    [LoggerMessage(EventId = 5405, Level = LogLevel.Warning, Message = "shadow.switch: the row stands but its audit event could not be recorded")]
    public static partial void AuditFailed(ILogger logger);

    [LoggerMessage(EventId = 5410, Level = LogLevel.Information, Message = "shadow.evidence_set: replaced by the owner, {Versions} version(s)")]
    public static partial void EvidenceSetReplaced(ILogger logger, int versions);

    [LoggerMessage(EventId = 5411, Level = LogLevel.Warning, Message = "shadow.evidence_set: none is stored and none can be named from the configuration snapshot (no eligible cell or no full snapshot): nothing is written")]
    public static partial void EvidenceSetMissing(ILogger logger);

    [LoggerMessage(EventId = 5412, Level = LogLevel.Information, Message = "shadow.evidence_set: registered as auto with the first write of the epoch, {Versions} version(s)")]
    public static partial void EvidenceSetRegistered(ILogger logger, int versions);
}
