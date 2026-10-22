using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>
/// Takes queued Lab runs and carries them out, at most <see cref="LabSettings.MaxParallelRuns"/> at a time (spec P5-02, criterion 9). The table is the queue: a run is claimed with <c>FOR UPDATE SKIP LOCKED</c>, so two dispatchers never share
/// a run. At the start of the host it ends the runs that this host left running as <c>interrupted</c> (D4); it never resumes them. A run that throws is the executor's to end; the dispatcher only keeps going. The host's hosted service calls
/// <see cref="RunAsync"/>; the tests call <see cref="DrainAsync"/>.
/// </summary>
public sealed class LabRunDispatcher(IServiceScopeFactory scopes, LabSettings settings, TimeProvider time, ILogger<LabRunDispatcher> logger)
{
    /// <summary>The name this host claims runs under; a restart of the same host finds its own interrupted runs.</summary>
    public const string Host = "xd-api";

    private readonly List<Task> _running = [];

    /// <summary>Ends the runs this host had running; call once before <see cref="RunAsync"/>.</summary>
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<ILabRunStore>();
        var progress = scope.ServiceProvider.GetRequiredService<ILabProgress>();
        var interrupted = await store.InterruptRunningAsync(Host, time.GetUtcNow(), cancellationToken);
        foreach (var id in interrupted)
        {
            LabRunLog.Interrupted(logger, id);
            await progress.FinishedAsync(new LabRunFinished(id, LabRunStates.Code(LabRunState.Interrupted)), cancellationToken);
        }
    }

    /// <summary>The loop of the host: look for queued runs every poll interval until the host stops. Runs still going when it stops are left running (the next start marks them interrupted).</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await GuardedAsync(RecoverAsync, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await GuardedAsync(async token => await FillSlotsAsync(token), stoppingToken);
                await Task.Delay(settings.PollInterval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    /// <summary>A database that is down for a moment must not end the loop (and with it the host): the failure is logged and the next tick tries again.</summary>
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
            LabRunLog.DispatcherFailed(logger, ex);
        }
    }

    /// <summary>For tests: claims and runs until nothing is queued and nothing is running.</summary>
    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var claimed = await FillSlotsAsync(cancellationToken);
            Task[] going;
            lock (_running)
            {
                going = [.. _running];
            }

            if (going.Length == 0 && claimed == 0)
            {
                return;
            }

            if (going.Length > 0)
            {
                await Task.WhenAny(going);
            }
        }
    }

    /// <summary>Claims runs while there is a free slot and a queued run; returns how many were started.</summary>
    private async Task<int> FillSlotsAsync(CancellationToken cancellationToken)
    {
        var started = 0;
        while (true)
        {
            lock (_running)
            {
                _running.RemoveAll(t => t.IsCompleted);
                if (_running.Count >= settings.MaxParallelRuns)
                {
                    return started;
                }
            }

            LabRun? run;
            await using (var scope = scopes.CreateAsyncScope())
            {
                run = await scope.ServiceProvider.GetRequiredService<ILabRunStore>().ClaimNextAsync(Host, time.GetUtcNow(), cancellationToken);
            }

            if (run is null)
            {
                return started;
            }

            started++;
            var task = Task.Run(() => ExecuteAsync(run, cancellationToken), CancellationToken.None);
            lock (_running)
            {
                _running.Add(task);
            }
        }
    }

    private async Task ExecuteAsync(LabRun run, CancellationToken hostStop)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ILabRunExecutor>().ExecuteAsync(run, hostStop);
        }
        catch (OperationCanceledException) when (hostStop.IsCancellationRequested)
        {
            // shutting down: the run stays running and is marked interrupted at the next start
        }
#pragma warning disable CA1031 // one failed run must not stop the dispatcher; the executor has ended the run as far as the database allowed
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LabRunLog.Crashed(logger, run.Id, run.Stage ?? "claimed", ex);
        }
    }
}
