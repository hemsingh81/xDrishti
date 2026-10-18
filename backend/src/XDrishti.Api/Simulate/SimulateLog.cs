namespace XDrishti.Api.Simulate;

/// <summary>
/// The log lines of the <c>simulate</c> verb (spec P3-04b2, D8; event ids 4405 to 4409: 4401 is the compiler warm-up, 4402 to 4404 the handler's lines, which are emitted in the same run; unique across the solution, asserted by the architecture
/// tests). Ids, codes, counts and the run id only: never a password, a connection string, a path, a bar, a price or the text of a strategy.
/// </summary>
internal static partial class SimulateLog
{
    [LoggerMessage(EventId = 4405, Level = LogLevel.Information, Message = "Simulate verb started: strategy {StrategyId}, universe {UniverseKind} of {Entries} entries, {From:yyyy-MM-dd} to {To:yyyy-MM-dd}")]
    public static partial void Started(ILogger logger, string strategyId, string universeKind, int entries, DateOnly from, DateOnly to);

    [LoggerMessage(EventId = 4406, Level = LogLevel.Warning, Message = "Simulate verb refused: {Code}, {Problems} problems")]
    public static partial void Refused(ILogger logger, string code, int problems);

    [LoggerMessage(EventId = 4407, Level = LogLevel.Information, Message = "Simulate verb wrote export {RunId}: {Files} files, {Bytes} bytes")]
    public static partial void Written(ILogger logger, string runId, int files, long bytes);

    [LoggerMessage(EventId = 4408, Level = LogLevel.Information, Message = "Simulate verb: export {RunId} was already there and is unchanged")]
    public static partial void Unchanged(ILogger logger, string runId);

    [LoggerMessage(EventId = 4409, Level = LogLevel.Error, Message = "Simulate verb failed: {ExceptionType}")]
    public static partial void Failed(ILogger logger, string exceptionType);
}
