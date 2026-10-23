using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Planning;

/// <summary>Structured log events of the wired plan builder and the suggestions (P7-09; ids 4740 to 4749). Counts and codes only: no price, no account id, no secret.</summary>
internal static partial class PlanWiringLog
{
    [LoggerMessage(EventId = 4740, Level = LogLevel.Information, Message = "plan.wired {ForDate}: {Flags} banner flag(s), {Accounts} trading account(s), {Allocations} allocation row(s)")]
    public static partial void Wired(ILogger logger, DateOnly forDate, int flags, int accounts, int allocations);

    [LoggerMessage(EventId = 4741, Level = LogLevel.Warning, Message = "plan.context_failed {ForDate}: {Code}")]
    public static partial void ContextFailed(ILogger logger, DateOnly forDate, string code);

    [LoggerMessage(EventId = 4742, Level = LogLevel.Information, Message = "suggestions.generated plan {PlanId} ({ForDate}): {Produced} produced, {Stored} new")]
    public static partial void Generated(ILogger logger, long planId, DateOnly forDate, int produced, int stored);

    [LoggerMessage(EventId = 4743, Level = LogLevel.Information, Message = "suggestions.no_plan {ForDate}")]
    public static partial void NoPlan(ILogger logger, DateOnly? forDate);
}
