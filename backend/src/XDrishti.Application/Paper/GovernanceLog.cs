using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Paper;

/// <summary>Structured log events of the configuration governance (P10-05; ids 5240 to 5245). Hash prefixes, key groups and classes only: never a value, an account or an amount.</summary>
internal static partial class GovernanceLog
{
    [LoggerMessage(EventId = 5240, Level = LogLevel.Information, Message = "paper.config_snapshot {Hash}: {Changes} change(s), outcome {Outcome}")]
    public static partial void Snapshot(ILogger logger, string hash, int changes, string outcome);

    [LoggerMessage(EventId = 5241, Level = LogLevel.Warning, Message = "paper.config_change {Group} class {Class} effect {Effect}")]
    public static partial void Change(ILogger logger, string group, string @class, string effect);

    [LoggerMessage(EventId = 5242, Level = LogLevel.Warning, Message = "paper.epoch_started kind {Kind} on {StartedOn} hash {Hash}")]
    public static partial void EpochStarted(ILogger logger, string kind, DateOnly startedOn, string hash);

    [LoggerMessage(EventId = 5243, Level = LogLevel.Error, Message = "paper.config_unreadable: {Groups}; no change claimed")]
    public static partial void Unreadable(ILogger logger, string groups);

    [LoggerMessage(EventId = 5244, Level = LogLevel.Information, Message = "paper.epoch_restart by {Actor} on {StartedOn}")]
    public static partial void Restart(ILogger logger, string actor, DateOnly startedOn);

    [LoggerMessage(EventId = 5245, Level = LogLevel.Information, Message = "paper.epoch_note on epoch {EpochId} by {Actor}")]
    public static partial void Note(ILogger logger, long epochId, string actor);
}
