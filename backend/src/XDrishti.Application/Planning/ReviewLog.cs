using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Planning;

/// <summary>Structured log events of the plan review (P7-10; ids 4770 to 4779). Ids, counts, codes and flags only: never a reason, a confirmation text, an actor or an account.</summary>
internal static partial class ReviewLog
{
    [LoggerMessage(EventId = 4770, Level = LogLevel.Information, Message = "plans.review.approved {ItemId}: {Overridden} checks approved despite")]
    public static partial void Approved(ILogger logger, long itemId, int overridden);

    [LoggerMessage(EventId = 4771, Level = LogLevel.Information, Message = "plans.review.modified {ItemId}")]
    public static partial void Modified(ILogger logger, long itemId);

    [LoggerMessage(EventId = 4772, Level = LogLevel.Information, Message = "plans.review.rejected {ItemId}")]
    public static partial void Rejected(ILogger logger, long itemId);

    [LoggerMessage(EventId = 4773, Level = LogLevel.Information, Message = "plans.review.approve_all {PlanId}: {Approved} approved, {Skipped} left for the owner")]
    public static partial void ApproveAll(ILogger logger, long planId, int approved, int skipped);

    [LoggerMessage(EventId = 4774, Level = LogLevel.Warning, Message = "plans.review.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 4775, Level = LogLevel.Information, Message = "plans.review.cycle {Plans} plans: {Auto} approved by rule, {Lapsed} lapsed")]
    public static partial void Cycle(ILogger logger, int plans, int auto, int lapsed);

    [LoggerMessage(EventId = 4776, Level = LogLevel.Information, Message = "plans.review.suggestion_seen {SuggestionId}")]
    public static partial void SuggestionSeen(ILogger logger, long suggestionId);
}
