using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Confidence;

/// <summary>Structured log events of the readiness scorecard (CF-11 part 1; ids 7300 to 7310). Counts, gate letters and codes only: no trade rows, no figures of a strategy and no secrets.</summary>
internal static partial class ReadinessLog
{
    [LoggerMessage(EventId = 7300, Level = LogLevel.Information, Message = "confidence.readiness.snapshot: {Strategies} strateg(ies), {Stored} stored, {AlreadyDone} already done today, {Failed} failed")]
    public static partial void Snapshot(ILogger logger, int strategies, int stored, int alreadyDone, int failed);

    [LoggerMessage(EventId = 7301, Level = LogLevel.Warning, Message = "confidence.readiness.snapshot: a strategy's scorecard threw {ExceptionType}; the run goes on and the strategy is repeated next time")]
    public static partial void StrategyThrew(ILogger logger, string exceptionType);

    [LoggerMessage(EventId = 7302, Level = LogLevel.Information, Message = "confidence.readiness.switch: row {Row} appended, enforce {Enforce} (1 = on)")]
    public static partial void Switched(ILogger logger, long row, int enforce);

    [LoggerMessage(EventId = 7303, Level = LogLevel.Information, Message = "confidence.readiness.gate {Gate}: passed {Passed} (1 = yes), enforced {Enforced} (1 = yes), refused {Refused} (1 = yes), {FailingRows} failing row(s)")]
    public static partial void Gate(ILogger logger, string gate, int passed, int enforced, int refused, int failingRows);

    [LoggerMessage(EventId = 7304, Level = LogLevel.Warning, Message = "confidence.readiness.gate {Gate}: the scorecard could not be read ({ExceptionType}); it counts as a fail")]
    public static partial void GateUnreadable(ILogger logger, string gate, string exceptionType);

    [LoggerMessage(EventId = 7305, Level = LogLevel.Warning, Message = "confidence.readiness.gate {Gate}: the enforce switch could not be read ({ExceptionType}); it is read as ON")]
    public static partial void SwitchUnreadable(ILogger logger, string gate, string exceptionType);

    [LoggerMessage(EventId = 7306, Level = LogLevel.Warning, Message = "confidence.readiness.gate {Gate}: the gate snapshot could not be stored ({ExceptionType}); the move carries no snapshot id")]
    public static partial void SnapshotNotStored(ILogger logger, string gate, string exceptionType);

    [LoggerMessage(EventId = 7307, Level = LogLevel.Warning, Message = "confidence.readiness.refused {Code}")]
    public static partial void Refused(ILogger logger, string code);

    [LoggerMessage(EventId = 7308, Level = LogLevel.Warning, Message = "confidence.readiness.integrity: the capture-integrity criterion could not be read ({ExceptionType}); the hard fail reads unavailable, which counts as a fail")]
    public static partial void IntegrityUnreadable(ILogger logger, string exceptionType);

    [LoggerMessage(EventId = 7309, Level = LogLevel.Warning, Message = "confidence.readiness.forward_tiers: the day bars of one instrument could not be read ({ExceptionType}); its {Trades} forward trade(s) count as tier unavailable")]
    public static partial void ForwardBarsUnread(ILogger logger, string exceptionType, int trades);

    [LoggerMessage(EventId = 7310, Level = LogLevel.Warning, Message = "confidence.readiness.forward_tiers: the forward trades could not be tiered ({ExceptionType}); the set reads unavailable, which counts as a fail")]
    public static partial void ForwardTiersUnreadable(ILogger logger, string exceptionType);

    [LoggerMessage(EventId = 7311, Level = LogLevel.Information, Message = "confidence.readiness.pack_gate: row {Row} appended, status {Status}, {Trades} trade(s)")]
    public static partial void PackGateRecorded(ILogger logger, long row, string status, int trades);

    [LoggerMessage(EventId = 7312, Level = LogLevel.Warning, Message = "confidence.readiness.pack_gate: the owner's record could not be read ({ExceptionType}); row 15's 20-trade term reads unavailable, which counts as a fail")]
    public static partial void PackGateUnreadable(ILogger logger, string exceptionType);
}
