using Microsoft.Extensions.Logging;

namespace XDrishti.Application.Research;

/// <summary>
/// Structured log events of a simulation run (event ids 4402 to 4404: unique across the solution, asserted by the architecture tests; 4401 is the compiler warm-up line). Ids, symbols, counts, reasons and milliseconds only: never a price,
/// a bar or the text of a strategy. The times of the phases are logged and never stored in the record, which reads no clock.
/// </summary>
internal static partial class SimulationLog
{
    [LoggerMessage(EventId = 4402, Level = LogLevel.Information, Message = "Simulation run started: strategy {StrategyId}, universe '{Universe}' of {Instruments} instruments, {From:yyyy-MM-dd} to {To:yyyy-MM-dd}, {Warnings} compile warnings, costs {Costs}")]
    public static partial void RunStarted(ILogger logger, string strategyId, string universe, int instruments, DateOnly from, DateOnly to, int warnings, string costs);

    [LoggerMessage(EventId = 4403, Level = LogLevel.Information, Message = "Simulation run: instrument {Symbol} ({InstrumentId}) skipped, {Reason}")]
    public static partial void InstrumentSkipped(ILogger logger, string symbol, Guid instrumentId, string reason);

    [LoggerMessage(EventId = 4404, Level = LogLevel.Information, Message = "Simulation run {RunKey} finished: {Run} instruments run, {Skipped} skipped, {Trades} trades; read {ReadMs} ms, replay {ReplayMs} ms, assembly {AssemblyMs} ms")]
    public static partial void RunFinished(ILogger logger, string runKey, int run, int skipped, int trades, long readMs, long replayMs, long assemblyMs);
}
