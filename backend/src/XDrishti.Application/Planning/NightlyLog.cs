using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Planning;

/// <summary>Structured log events of the nightly pipeline (P7-08; ids 4680 to 4689). Gate ids, outcomes, dates and counts only: never a reason text from an account or a secret.</summary>
internal static partial class NightlyLog
{
    [LoggerMessage(EventId = 4680, Level = LogLevel.Information, Message = "nightly.published {ForDate}: plan {PlanId}, attempt {Attempt}")]
    public static partial void Published(ILogger logger, DateOnly forDate, long planId, int attempt);

    [LoggerMessage(EventId = 4681, Level = LogLevel.Warning, Message = "nightly.failed_closed {ForDate}: gate {Gate} {Status}, attempt {Attempt}")]
    public static partial void FailedClosed(ILogger logger, DateOnly forDate, string gate, string status, int attempt);

    [LoggerMessage(EventId = 4682, Level = LogLevel.Information, Message = "nightly.repeat {ForDate}: already published as plan {PlanId}, nothing done")]
    public static partial void Repeat(ILogger logger, DateOnly forDate, long planId);

    [LoggerMessage(EventId = 4683, Level = LogLevel.Information, Message = "nightly.dry_run {ForDate}: {Outcome}, nothing written")]
    public static partial void DryRun(ILogger logger, DateOnly forDate, string outcome);

    [LoggerMessage(EventId = 4684, Level = LogLevel.Warning, Message = "nightly.gate_error {Gate}: {ErrorType}")]
    public static partial void GateError(ILogger logger, string gate, string errorType);
}
