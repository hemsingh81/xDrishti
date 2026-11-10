using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Application.Abstractions.SimAll;
using XDrishti.Domain.Market;
using XDrishti.Domain.Research.SimAll;

namespace XDrishti.Application.SimAll;

/// <summary>
/// Works the active simulate-everything job (spec AU-12, ADR 0248). The tables are the queue and the memory: a start of the host puts the chunks it left running back to pending, then <see cref="TickAsync"/> goes on at the first unfinished chunk. A tick (every poll interval)
/// looks at the yield rules (market hours, free disk, a running bar-history job, the backfill), pauses or resumes the job by them, writes the sweep ledger rows, and runs <see cref="SimAllSettings.Workers"/> workers that each claim one chunk at a time: the limit on CPU and memory
/// is the number of workers, since a chunk reads one instrument's bars. It reads stored bars only, so it makes no broker call, and it builds, places and references no order. Off by default: with no job there is nothing to do.
/// </summary>
public sealed class SimAllRunner(IServiceScopeFactory scopes, SimAllSettings settings, TimeProvider time, ILogger<SimAllRunner> logger)
{
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
            return;
        }

        var reason = await YieldReasonAsync(scope.ServiceProvider, true, cancellationToken);
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

    private async Task WorkerAsync(SimAllJob job, CancellationToken hostStop)
    {
        while (!hostStop.IsCancellationRequested)
        {
            await using var scope = scopes.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<ISimAllStore>();
            if (await YieldReasonAsync(scope.ServiceProvider, false, hostStop) is { } reason)
            {
                await PauseAsync(store, job.Id, reason, hostStop);
                return;
            }

            if (await store.ClaimChunkAsync(job.Id, time.GetUtcNow(), hostStop) is not { } chunk)
            {
                return;
            }

            var now = Interlocked.Increment(ref _active);
            InterlockedMax(ref _peak, now);
            try
            {
                var result = await scope.ServiceProvider.GetRequiredService<ISimAllChunkExecutor>().ExecuteAsync(job, chunk, hostStop);
                await store.FinishChunkAsync(chunk, result, time.GetUtcNow(), CancellationToken.None);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }

    /// <summary>Why the job should wait now, or null. Cheap checks first; the backfill is asked once per tick, not per chunk.</summary>
    private async Task<string?> YieldReasonAsync(IServiceProvider services, bool includeBackfill, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        if (!settings.RunInMarketHours && SimAllPause.InMarketHours(now, (await services.GetRequiredService<ITradingCalendar>().GetAsync(cancellationToken)).IsTradingDay(IndiaTime.Today(now))))
        {
            return SimAllPause.MarketHours;
        }

        if (services.GetRequiredService<IFreeDiskProbe>().FreeBytes() is not { } free || free < settings.MinFreeBytes)
        {
            return SimAllPause.LowDisk;
        }

        var gate = services.GetRequiredService<ISimAllBackfillGate>();
        if (await gate.DataJobRunningAsync(cancellationToken))
        {
            return SimAllPause.DataJob;
        }

        return includeBackfill && await gate.IncompleteReasonAsync(cancellationToken) is not null ? SimAllPause.Backfill : null;
    }

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
