using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Replay;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Application.Learning.SimTraining;
using XDrishti.Domain.Research.SimAll;

namespace XDrishti.Application.SimAll;

/// <summary>
/// Works the active simulate-everything job (spec AU-12, ADR 0248). The tables are the queue and the memory: a start of the host puts the chunks it left running back to pending, then <see cref="TickAsync"/> goes on at the first unfinished chunk. A tick (every poll interval)
/// looks at the yield rules (market hours, free disk, a running bar-history job, the backfill), pauses or resumes the job by them, writes the sweep ledger rows, and runs <see cref="SimAllSettings.Workers"/> workers that each claim one chunk at a time: the limit on CPU and memory
/// is the number of workers, since a chunk reads one instrument's bars. It reads stored bars only, so it makes no broker call, and it builds, places and references no order. Off by default: with no job there is nothing to do.
/// </summary>
public sealed class SimAllRunner(IServiceScopeFactory scopes, SimAllSettings settings, TimeProvider time, ILogger<SimAllRunner> logger)
{
    /// <summary>How long past the chunk time limit a chunk may stay <c>running</c> before the tick takes it back.</summary>
    private static readonly TimeSpan StaleMargin = TimeSpan.FromMinutes(5);

    /// <summary>The backfill check reads the database for every tracked instrument, so it is asked at most this often.</summary>
    private static readonly TimeSpan BackfillEvery = TimeSpan.FromMinutes(1);

    private DateTimeOffset? _backfillCheckedAt;
    private bool _backfillIncomplete;
    private readonly HashSet<Guid> _trainingAttempted = [];
    private int _active;
    private int _peak;

    /// <summary>The most chunks that ran at once since the runner was made (for the test of the limit).</summary>
    public int PeakConcurrency => Volatile.Read(ref _peak);

    /// <summary>The loop of the host: recover once, then look every poll interval until the host stops.</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await GuardedAsync(RecoverAsync, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await GuardedAsync(TickAsync, stoppingToken);
                await Task.Delay(settings.PollInterval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    /// <summary>Puts the chunks a stopped host left running back to pending; call once before the first tick.</summary>
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var redone = await scope.ServiceProvider.GetRequiredService<ISimAllStore>().RedoRunningChunksAsync(cancellationToken);
        if (redone > 0)
        {
            SimAllLog.Redo(logger, redone);
        }
    }

    /// <summary>One look at the active job: pause or resume by the yield rules, then work until the job pauses, ends or the host stops.</summary>
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ISimAllStore>();
        if (await store.GetActiveAsync(cancellationToken) is not { } job)
        {
            await TrainAfterJobAsync(scope.ServiceProvider, store, cancellationToken);
            return;
        }

        var now0 = time.GetUtcNow();
        var stale = await store.RedoStaleChunksAsync(now0 - TimeSpan.FromMinutes(settings.ChunkTimeoutMinutes) - StaleMargin, cancellationToken); // a worker that died inside a live host
        if (stale > 0)
        {
            SimAllLog.Redo(logger, stale);
        }

        var reason = (await YieldAsync(scope.ServiceProvider, true, cancellationToken)).Reason;
        if (job.State == SimAllStates.Paused)
        {
            if (!SimAllPause.LiftsItself(job.PauseReason) || reason is not null || !await store.ResumeAsync(job.Id, job.PauseReason, time.GetUtcNow(), cancellationToken))
            {
                return;
            }

            SimAllLog.Resumed(logger, job.Id);
        }
        else if (reason is not null)
        {
            await PauseAsync(store, job.Id, reason, cancellationToken);
            return;
        }

        await WriteSweepsAsync(scope.ServiceProvider, store, job, cancellationToken);
        var workers = Enumerable.Range(0, settings.Workers).Select(_ => Task.Run(() => WorkerAsync(job, cancellationToken), CancellationToken.None)).ToArray();
        await Task.WhenAll(workers);
        cancellationToken.ThrowIfCancellationRequested();
        if (await store.CompleteIfFinishedAsync(job.Id, time.GetUtcNow(), cancellationToken) is { } end)
        {
            SimAllLog.Finished(logger, job.Id, end);
        }
    }

    /// <summary>
    /// After a job has completed, trains from it once per host start (AU-13): only when the owner turned <c>Training:Enabled</c> on, and only while the yield rules (market hours, disk, a bar-history job) allow. The handler is idempotent by input hash, so a restart that repeats the
    /// attempt writes nothing. A failure is logged and not retried until the next start. It trains a challenger at most and places no order.
    /// </summary>
    private async Task TrainAfterJobAsync(IServiceProvider services, ISimAllStore store, CancellationToken cancellationToken)
    {
        if (services.GetService<SimTrainingSettings>() is not { Enabled: true } || await store.GetCurrentAsync(cancellationToken) is not { Job: { } last }
            || last.State is not (SimAllStates.Completed or SimAllStates.CompletedWithFailures) || _trainingAttempted.Contains(last.Id))
        {
            return;
        }

        var yield = await YieldAsync(services, false, cancellationToken);
        if (yield.Reason is not null)
        {
            return;
        }

        _trainingAttempted.Add(last.Id);

        // as for a chunk: the market opening is a deadline, and a watcher looks at disk and the bar-history job while training runs; either cancels it and the attempt is released, so it resumes at a later tick
        using var stop = new CancellationTokenSource();
        using var deadline = yield.Deadline is { } at ? new CancellationTokenSource(TimeSpan.FromTicks(Math.Max(0, (at - time.GetUtcNow()).Ticks)), time) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token, stop.Token);
        var watcher = WatchTrainingAsync(stop, linked.Token);
        try
        {
            await services.GetRequiredService<ICommandHandler<TrainFromSimulationCommand, SimTrainingView>>().HandleAsync(new TrainFromSimulationCommand(last.Id), linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _trainingAttempted.Remove(last.Id); // the market opened or a yield rule says wait: nothing was stored, a later tick tries again
        }
        catch (OperationCanceledException)
        {
            _trainingAttempted.Remove(last.Id);
            throw;
        }
#pragma warning disable CA1031 // a failed training must not stop the job loop; it is logged and tried again at the next start
        catch (Exception ex)
#pragma warning restore CA1031
        {
            SimTrainingLog.Failed(logger, last.Id, ex);
        }
        finally
        {
            await linked.CancelAsync();
            await watcher;
        }
    }

    /// <summary>Looks at the yield rules (disk, a running bar-history job) every poll interval while training runs and cancels it when one says wait.</summary>
    private async Task WatchTrainingAsync(CancellationTokenSource stop, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(settings.PollInterval, time, token);
                await using var scope = scopes.CreateAsyncScope();
                if ((await YieldAsync(scope.ServiceProvider, false, token)).Reason is not null)
                {
                    await stop.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // training ended or the host stops
        }
    }

    private async Task WorkerAsync(SimAllJob job, CancellationToken hostStop)
    {
        while (!hostStop.IsCancellationRequested)
        {
            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<ISimAllStore>();
            var yield = await YieldAsync(scope.ServiceProvider, false, hostStop);
            if (yield.Reason is { } reason)
            {
                await PauseAsync(store, job.Id, reason, hostStop);
                return;
            }

            if (await store.ClaimChunkAsync(job.Id, time.GetUtcNow(), hostStop) is not { } chunk)
            {
                return;
            }

            var concurrent = Interlocked.Increment(ref _active);
            InterlockedMax(ref _peak, concurrent);
            try
            {
                // the market opening is a deadline for the chunk in flight: it is cancelled and put back, not left to run into the session (a deadline that has just passed cancels at once; a negative delay would throw)
                using var open = yield.Deadline is { } at ? new CancellationTokenSource(TimeSpan.FromTicks(Math.Max(0, (at - time.GetUtcNow()).Ticks)), time) : new CancellationTokenSource();
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(hostStop, open.Token);
                var outcome = await RunChunkAsync(scope.ServiceProvider, store, job, chunk, linked.Token, hostStop);
                if (outcome != ChunkOutcome.Recorded)
                {
                    if (outcome == ChunkOutcome.MarketOpened)
                    {
                        await PauseAsync(store, job.Id, SimAllPause.MarketHours, hostStop);
                    }

                    return; // a result that cannot be recorded ends this worker for the tick: the chunk is pending again and the next tick tries it, not a tight loop
                }
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    /// <summary>Runs one claimed chunk and records how it ended. A crash of the executor fails the chunk; a result that cannot be recorded puts the chunk back to pending (and if even that fails, the stale sweep of the next tick does). The outcome says whether the result was recorded, the market opened under the chunk (put back) or the result could not be recorded (put back).</summary>
    private async Task<ChunkOutcome> RunChunkAsync(IServiceProvider services, ISimAllStore store, SimAllJob job, SimAllChunk chunk, CancellationToken token, CancellationToken hostStop)
    {
        SimAllChunkResult result;
        try
        {
            result = await services.GetRequiredService<ISimAllChunkExecutor>().ExecuteAsync(job, chunk, token);
        }
        catch (OperationCanceledException) when (hostStop.IsCancellationRequested)
        {
            throw; // the host stops: the chunk stays running and the next start takes it back
        }
        catch (OperationCanceledException)
        {
            await ReleaseQuietlyAsync(store, chunk);
            return ChunkOutcome.MarketOpened;
        }
#pragma warning disable CA1031 // an unexpected failure of one chunk is recorded on the chunk; the job goes on
        catch (Exception ex)
#pragma warning restore CA1031
        {
            SimAllLog.ChunkCrashed(logger, chunk.JobId, ex);
            result = new SimAllChunkResult(SimAllStates.Failed, null, 0, 0, 0, SimAllErrors.ChunkCrashedCode, "The chunk failed unexpectedly; see the log.");
        }

        try
        {
            await store.FinishChunkAsync(chunk, result, time.GetUtcNow(), CancellationToken.None);
        }
#pragma warning disable CA1031 // the result could not be recorded: the chunk goes back to pending and is run again
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // shortcut: a chunk whose result keeps failing to record is retried at every tick with no attempt count and no backoff (ADR 0248 point 3). Add a per-chunk attempt count (a column on sim_all_chunks) and a growing delay
            // the first time one chunk is seen failing to record more than a handful of times in the log (event RecordFailed), since each retry re-runs the whole chunk and holds a worker.
            SimAllLog.RecordFailed(logger, chunk.JobId, ex);
            await ReleaseQuietlyAsync(store, chunk);
            return ChunkOutcome.RecordFailed;
        }

        return ChunkOutcome.Recorded;
    }

    private async Task ReleaseQuietlyAsync(ISimAllStore store, SimAllChunk chunk)
    {
        try
        {
            await store.ReleaseChunkAsync(chunk, CancellationToken.None);
        }
#pragma warning disable CA1031 // the tick's stale sweep takes the chunk back when this fails too
        catch (Exception ex)
#pragma warning restore CA1031
        {
            SimAllLog.RecordFailed(logger, chunk.JobId, ex);
        }
    }

    /// <summary>Why the job should wait now, or null, and the instant the market window next opens (a deadline for a chunk in flight). Cheap checks first; the backfill is asked once a minute and only by the tick.</summary>
    private async Task<Yield> YieldAsync(IServiceProvider services, bool includeBackfill, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (services.GetRequiredService<IFreeDiskProbe>().FreeBytes() is not { } free || free < settings.MinFreeBytes)
        {
            return new Yield(SimAllPause.LowDisk, null);
        }

        DateTimeOffset? deadline = null;
        if (!settings.RunInMarketHours)
        {
            var calendar = await services.GetRequiredService<ITradingCalendar>().GetAsync(cancellationToken);
            deadline = SimAllPause.NextWindowStart(calendar, now);
            if (SimAllPause.InMarketHours(calendar, now) || (deadline is { } next && next - now < TimeSpan.FromMinutes(settings.ChunkTimeoutMinutes)))
            {
                return new Yield(SimAllPause.MarketHours, null); // no chunk is started that could run into the session
            }
        }

        var gate = services.GetRequiredService<ISimAllBackfillGate>();
        if (await gate.DataJobRunningAsync(cancellationToken))
        {
            return new Yield(SimAllPause.DataJob, null);
        }

        // a system replay (SR-01) in flight: the two jobs read the same bars and do not run side by side. The job pauses under the reason of a data job (a stored reason of its own would need a migration) and goes on when the replay has finished; the replay does not wait for a paused job, so neither waits for the other.
        if (services.GetService<IReplayReads>() is { } replays && await replays.AnyRunningAsync(cancellationToken))
        {
            return new Yield(SimAllPause.DataJob, null);
        }

        if (includeBackfill)
        {
            if (_backfillCheckedAt is not { } checkedAt || now - checkedAt >= BackfillEvery)
            {
                _backfillIncomplete = await gate.IncompleteReasonAsync(cancellationToken) is not null;
                _backfillCheckedAt = now;
            }

            if (_backfillIncomplete)
            {
                return new Yield(SimAllPause.Backfill, null);
            }
        }

        return new Yield(null, deadline);
    }

    private enum ChunkOutcome
    {
        Recorded,
        MarketOpened,
        RecordFailed,
    }

    private sealed record Yield(string? Reason, DateTimeOffset? Deadline);

    private async Task PauseAsync(ISimAllStore store, Guid id, string reason, CancellationToken cancellationToken)
    {
        if (await store.PauseAsync(id, reason, time.GetUtcNow(), cancellationToken))
        {
            SimAllLog.Paused(logger, id, reason);
        }
    }

    /// <summary>One <c>training_sweep</c> ledger row per strategy version, before any chunk runs (ADR 0243 point 3). The row id comes from the job and the version, so a restart writes nothing twice.</summary>
    private static async Task WriteSweepsAsync(IServiceProvider services, ISimAllStore store, SimAllJob job, CancellationToken cancellationToken)
    {
        var ledger = services.GetRequiredService<ITestLedger>();
        foreach (var sweep in await store.ListSweepsAsync(job.Id, cancellationToken))
        {
            var key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"sim-all|{job.Id:D}|{sweep.StrategyHash}"))));
            var id = new Guid(Convert.FromHexString(key[..32]));
            await ledger.AppendAsync(new LedgerRow(id, key, sweep.StrategyId, sweep.Version, sweep.StrategyHash, null, "all tracked instruments", job.WindowFrom, job.WindowTo, "training", job.CreatedAt, LedgerRow.TrainingSweepKind, 0), cancellationToken);
        }
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        do
        {
            seen = Volatile.Read(ref target);
        }
        while (value > seen && Interlocked.CompareExchange(ref target, value, seen) != seen);
    }

    private async Task GuardedAsync(Func<CancellationToken, Task> step, CancellationToken stoppingToken)
    {
        try
        {
            await step(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // the loop of the host must survive a failed step
        catch (Exception ex)
#pragma warning restore CA1031
        {
            SimAllLog.LoopFailed(logger, ex);
        }
    }
}
