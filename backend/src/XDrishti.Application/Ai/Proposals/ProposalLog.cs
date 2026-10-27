using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Ai.Proposals;

/// <summary>Log events 6180 to 6189 (spec P9-08 AC 15). Ids, kinds, counts and finding codes only: never a reason, a payload value or a prompt.</summary>
internal static partial class ProposalLog
{
    [LoggerMessage(EventId = 6180, Level = LogLevel.Information, Message = "assistant.proposals.pack cells={Cells} baskets={Baskets} candidates={Candidates} unavailable={Unavailable} hash={Hash}")]
    public static partial void Pack(ILogger logger, int cells, int baskets, int candidates, int unavailable, string hash);

    [LoggerMessage(EventId = 6181, Level = LogLevel.Information, Message = "assistant.proposals.attempt attempt={Attempt} items={Items} refused={Refused} ms={Ms}")]
    public static partial void Attempt(ILogger logger, int attempt, int items, int refused, int ms);

    [LoggerMessage(EventId = 6182, Level = LogLevel.Information, Message = "assistant.proposals.run state={State} reason={Reason} stored={Stored} refused={Refused} codes={Codes}")]
    public static partial void Run(ILogger logger, string state, string? reason, int stored, int refused, string codes);

    [LoggerMessage(EventId = 6183, Level = LogLevel.Information, Message = "assistant.proposals.stored id={Id} kind={Kind}")]
    public static partial void Stored(ILogger logger, long id, string kind);

    [LoggerMessage(EventId = 6184, Level = LogLevel.Warning, Message = "assistant.proposals.model_failed attempt={Attempt}")]
    public static partial void ModelFailed(ILogger logger, int attempt);

    [LoggerMessage(EventId = 6185, Level = LogLevel.Information, Message = "assistant.proposals.decided id={Id} decision={Decision} auto={Auto}")]
    public static partial void Decided(ILogger logger, long id, string decision, bool auto);

    [LoggerMessage(EventId = 6186, Level = LogLevel.Information, Message = "assistant.proposals.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 6187, Level = LogLevel.Information, Message = "assistant.proposals.auto id={Id} accepted={Accepted} reason={Reason}")]
    public static partial void Auto(ILogger logger, long id, bool accepted, string reason);

    [LoggerMessage(EventId = 6188, Level = LogLevel.Information, Message = "assistant.proposals.stale id={Id} code={Code}")]
    public static partial void Stale(ILogger logger, long id, string code);

    [LoggerMessage(EventId = 6189, Level = LogLevel.Error, Message = "assistant.proposals.decision_not_written id={Id} result={Result}")]
    public static partial void DecisionNotWritten(ILogger logger, long id, string result);
}
