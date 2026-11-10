using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.Lab;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Domain.Research.SimAll;

namespace XDrishti.Application.SimAll;

/// <summary>Carries out one chunk to its result; a seam so the runner is tested without a simulation. A stop of the host is an <see cref="OperationCanceledException"/>: the chunk stays running and is redone at the next start.</summary>
internal interface ISimAllChunkExecutor
{
    ValueTask<SimAllChunkResult> ExecuteAsync(SimAllJob job, SimAllChunk chunk, CancellationToken hostStop);
}

/// <summary>
/// One chunk: the stored strategy version over one instrument in the job's training window, by the same <c>RunSimulationQuery</c> and <c>StoreRunCommand</c> as every run (no second engine), with the purpose <c>training</c> in the request. The window ends before the hold-out and
/// its embargo, and the store refuses any other (ADR 0243 point 6). The run key makes a repeat a no-op. Expected failures and an unexpected exception become a failed result with its reason; a chunk past <see cref="SimAllSettings.ChunkTimeoutMinutes"/> is failed too. No order
/// is built, placed or referenced here.
/// </summary>
internal sealed class SimAllChunkExecutor(
    IStrategyStore strategies,
    IStrategyChecker checker,
    ISimulationEngine engine,
    ICostSheetStore sheets,
    LabSettings labSettings,
    IQueryHandler<RunSimulationQuery, Result<SimulationRun>> simulation,
    ICommandHandler<StoreRunCommand, StoreOutcome> storeRun,
    IRunRecordWriter recordWriter,
    SimAllSettings settings,
    TimeProvider time,
    ILogger<SimAllChunkExecutor> logger) : ISimAllChunkExecutor
{
    /// <summary>Who is recorded as the author of a stored training run.</summary>
    public const string Actor = "simulate-all";

    public async ValueTask<SimAllChunkResult> ExecuteAsync(SimAllJob job, SimAllChunk chunk, CancellationToken hostStop)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(chunk);
        var started = time.GetTimestamp();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(settings.ChunkTimeoutMinutes), time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(hostStop, timeout.Token);
        try
        {
            return await RunAsync(job, chunk, started, linked.Token);
        }
        catch (OperationCanceledException) when (hostStop.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Failed(started, SimAllErrors.ChunkTimeoutCode, "The chunk ran past " + settings.ChunkTimeoutMinutes + " minutes.");
        }
#pragma warning disable CA1031 // an unexpected failure of one chunk is recorded on the chunk; the job goes on
        catch (Exception ex)
#pragma warning restore CA1031
        {
            SimAllLog.ChunkCrashed(logger, chunk.JobId, ex);
            return Failed(started, SimAllErrors.ChunkCrashedCode, "The chunk failed unexpectedly; see the log.");
        }
    }

    private async ValueTask<SimAllChunkResult> RunAsync(SimAllJob job, SimAllChunk chunk, long started, CancellationToken cancellationToken)
    {
        var version = await strategies.GetByHashAsync(chunk.StrategyHash, cancellationToken);
        if (version is null)
        {
            return Failed(started, SimAllErrors.StrategyMissingCode, "The stored version of the strategy is not found.");
        }

        var check = checker.Check(version.SourceText);
        if (check.Description is not { } description)
        {
            return Failed(started, SimAllErrors.CompileFailedCode, "The strategy does not compile under today's rules.");
        }

        var sheet = await sheets.FindAsync(labSettings.CostSheetId, cancellationToken);
        var costs = sheet.IsSuccess && sheet.Value.Schedules.Count > 0 && sheet.Value.Schedules[0].EffectiveFrom <= job.WindowFrom ? CostChoice.Sheet(sheet.Value.Id) : CostChoice.Gross;
        var request = new SimulationRequest(chunk.StrategyId, new Dictionary<string, double>(), DefaultTimeframe(chunk.StrategyId, description).ToCode(), job.WindowFrom, job.WindowTo, [chunk.InstrumentId], null, costs, false, null, null, null, null, chunk.StrategyHash, RunPurpose.Training);
        var result = await simulation.HandleAsync(new RunSimulationQuery(request), cancellationToken);
        if (result.IsFailure)
        {
            return Failed(started, result.Error.Code, result.Error.Message);
        }

        var run = result.Value;
        var record = recordWriter.Write(run);
        cancellationToken.ThrowIfCancellationRequested();
        var stored = await storeRun.HandleAsync(new StoreRunCommand(run, record, Actor), cancellationToken);
        if (stored.IsFailure)
        {
            return Failed(started, stored.Error.Code, stored.Error.Message);
        }

        var millis = (long)time.GetElapsedTime(started).TotalMilliseconds;
        SimAllLog.Chunk(logger, chunk.JobId, SimAllStates.Done, millis, stored.Value.WasAlreadyStored ? "already_stored" : "stored");
        return new SimAllChunkResult(SimAllStates.Done, run.RunKey, run.Trades.Count + run.OpenAtEnd.Count, record.Json.Length, millis, null, null);
    }

    private SimAllChunkResult Failed(long started, string code, string message)
    {
        var millis = (long)time.GetElapsedTime(started).TotalMilliseconds;
        return new SimAllChunkResult(SimAllStates.Failed, null, 0, 0, millis, code.Length > 80 ? code[..80] : code, message.Length > 300 ? message[..300] : message);
    }

    private Timeframe DefaultTimeframe(string strategyId, StrategyDescription description) =>
        engine.BuiltIns.FirstOrDefault(b => string.Equals(b.Id, strategyId, StringComparison.Ordinal))?.DefaultTimeframe
        ?? (description.Timeframes.Contains(Timeframe.M15) || description.Timeframes.Count == 0 ? Timeframe.M15 : description.Timeframes[0]);
}
