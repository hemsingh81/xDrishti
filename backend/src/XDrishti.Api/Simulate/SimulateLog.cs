namespace XDrishti.Api.Simulate;

/// <summary>
/// The log lines of the <c>simulate</c> verb (spec P3-04b2, D8; event ids 4405 to 4411 (4410 and 4411 are the trade cards of P3-04c) and 4419 to 4422 (the verify-pack verb of P3-09): 4401 is the compiler warm-up, 4402 to 4404 the handler's lines, which are emitted in the same run; unique across the solution, asserted by the architecture
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

    [LoggerMessage(EventId = 4410, Level = LogLevel.Information, Message = "Simulate verb wrote {Cards} trade cards and the index: {Bytes} bytes")]
    public static partial void CardsWritten(ILogger logger, int cards, long bytes);

    [LoggerMessage(EventId = 4411, Level = LogLevel.Warning, Message = "Simulate verb: the trade cards are stale for instrument {Symbol}: {Difference}")]
    public static partial void CardsStale(ILogger logger, string symbol, string difference);

    [LoggerMessage(EventId = 4486, Level = LogLevel.Warning, Message = "Simulate verb: the recomputation for the trade cards does not give the recorded trade of instrument {Symbol}: first difference {Difference}")]
    public static partial void CardsMismatch(ILogger logger, string symbol, string difference);

    [LoggerMessage(EventId = 4419, Level = LogLevel.Information, Message = "Verify-pack verb wrote pack {PackId}: {Rows} rows, {Bytes} bytes")]
    public static partial void PackWritten(ILogger logger, string packId, int rows, long bytes);

    [LoggerMessage(EventId = 4420, Level = LogLevel.Information, Message = "Verify-pack verb: pack {PackId} was already there and is unchanged")]
    public static partial void PackUnchanged(ILogger logger, string packId);

    [LoggerMessage(EventId = 4421, Level = LogLevel.Information, Message = "Verify-pack check read {Rows} rows of the log; the pack is complete: {Complete}")]
    public static partial void CheckRead(ILogger logger, int rows, bool complete);

    [LoggerMessage(EventId = 4422, Level = LogLevel.Information, Message = "Verify-pack verb started: {Strategies} strategies, universe {UniverseKind} of {Entries} entries, {From:yyyy-MM-dd} to {To:yyyy-MM-dd}, {Count} random trades")]
    public static partial void PackStarted(ILogger logger, int strategies, string universeKind, int entries, DateOnly from, DateOnly to, int count);

    [LoggerMessage(EventId = 4423, Level = LogLevel.Information, Message = "Verify-pack assist read {Rows} rows on the second source {Source}: {Agree} agree_all, {Other} other")]
    public static partial void AssistRead(ILogger logger, int rows, string source, int agree, int other);

    [LoggerMessage(EventId = 4424, Level = LogLevel.Information, Message = "Verify-pack gate decided {Status} on the second source {Source}")]
    public static partial void GateDecided(ILogger logger, string status, string source);
}
