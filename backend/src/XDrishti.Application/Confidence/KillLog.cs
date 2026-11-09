using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Confidence;

/// <summary>Structured log events of the kill proposals (CF-12 part 12b; ids 7100 to 7106). Ids, rule codes and counts only: no trade rows and no secrets.</summary>
internal static partial class KillLog
{
    [LoggerMessage(EventId = 7100, Level = LogLevel.Information, Message = "confidence.kill.proposed {ProposalId}: rule {Rule}, {Cells} cell(s)")]
    public static partial void Proposed(ILogger logger, long proposalId, string rule, int cells);

    [LoggerMessage(EventId = 7101, Level = LogLevel.Information, Message = "confidence.kill.applied {ProposalId} by {Kind}: {Paused} cell(s) paused, {Skipped} skipped")]
    public static partial void Applied(ILogger logger, long proposalId, string kind, int paused, int skipped);

    [LoggerMessage(EventId = 7108, Level = LogLevel.Information, Message = "confidence.kill.dismissed {ProposalId} by the owner")]
    public static partial void Dismissed(ILogger logger, long proposalId);

    [LoggerMessage(EventId = 7102, Level = LogLevel.Warning, Message = "confidence.kill.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 7103, Level = LogLevel.Information, Message = "confidence.kill.switch: row {Row} appended, auto {Auto} (1 = on)")]
    public static partial void Switched(ILogger logger, long row, int auto);

    [LoggerMessage(EventId = 7104, Level = LogLevel.Information, Message = "confidence.kill.reenabled: a cell moved from paused to paper by the owner")]
    public static partial void ReEnabled(ILogger logger);

    [LoggerMessage(EventId = 7105, Level = LogLevel.Information, Message = "confidence.decay: {Strategies} strateg(ies), {Checked} checked, {AlreadyDone} already done today, {Proposed} proposal(s), {Applied} applied automatically, {Failed} failed")]
    public static partial void Run(ILogger logger, int strategies, int @checked, int alreadyDone, int proposed, int applied, int failed);

    [LoggerMessage(EventId = 7106, Level = LogLevel.Warning, Message = "confidence.decay: strategy evaluation failed with {Code}")]
    public static partial void StrategyFailed(ILogger logger, string code);

    [LoggerMessage(EventId = 7107, Level = LogLevel.Error, Message = "confidence.decay: strategy evaluation threw {ExceptionType}; the run goes on and the strategy is repeated next time")]
    public static partial void StrategyThrew(ILogger logger, string exceptionType);
}
