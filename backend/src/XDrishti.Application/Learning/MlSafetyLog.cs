using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Learning;

/// <summary>Structured log events of promotion, rollback and the ML switches (CF-10 part 10d-1; ids 7200 to 7207). Ids, codes and counts only: no rows, no scores and no secrets.</summary>
internal static partial class MlSafetyLog
{
    [LoggerMessage(EventId = 7200, Level = LogLevel.Information, Message = "learning.ml.rollback.proposed {ProposalId}: model {ModelId}, trigger {Trigger}")]
    public static partial void RollbackProposed(ILogger logger, long proposalId, long modelId, string trigger);

    [LoggerMessage(EventId = 7201, Level = LogLevel.Information, Message = "learning.ml.rollback.applied {ProposalId} by {Kind}: champion demoted {Demoted}")]
    public static partial void RollbackApplied(ILogger logger, long proposalId, string kind, bool demoted);

    [LoggerMessage(EventId = 7202, Level = LogLevel.Warning, Message = "learning.ml.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 7203, Level = LogLevel.Information, Message = "learning.ml.switch: row {Row} appended for {Name}, on {On} (1 = on)")]
    public static partial void Switched(ILogger logger, long row, string name, int on);

    [LoggerMessage(EventId = 7204, Level = LogLevel.Information, Message = "learning.ml.promoted: model {ModelId} by {Kind}")]
    public static partial void Promoted(ILogger logger, long modelId, string kind);

    [LoggerMessage(EventId = 7205, Level = LogLevel.Information, Message = "learning.ml.monitor: {Champions} champion(s), {Checked} evaluated, {AlreadyDone} already done today, {Proposed} proposal(s), {Rolled} rolled back automatically, {Promoted} promoted automatically, {Failed} failed")]
    public static partial void Run(ILogger logger, int champions, int @checked, int alreadyDone, int proposed, int rolled, int promoted, int failed);

    [LoggerMessage(EventId = 7206, Level = LogLevel.Warning, Message = "learning.ml.monitor: an evaluation failed with {Code}")]
    public static partial void ItemFailed(ILogger logger, string code);

    [LoggerMessage(EventId = 7207, Level = LogLevel.Error, Message = "learning.ml.monitor: an evaluation threw {ExceptionType}; the run goes on and the model is repeated next time")]
    public static partial void ItemThrew(ILogger logger, string exceptionType);
}
