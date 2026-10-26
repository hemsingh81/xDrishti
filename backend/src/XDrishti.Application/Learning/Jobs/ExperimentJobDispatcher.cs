using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning.Jobs;

/// <summary>
/// Takes the queued replay job and carries it out, ONE at a time (spec P8-18 Part B, decision D4). The table is the queue: a job is claimed with <c>FOR UPDATE SKIP LOCKED</c>. At the start of the host it ends the jobs this host left running as <c>interrupted</c>; it never
/// resumes one, never retries a failed one and never schedules one: a job exists only because the owner started it. The host's hosted service calls <see cref="RunAsync"/>; the tests call <see cref="DrainAsync"/>.
/// </summary>
public sealed class ExperimentJobDispatcher(IServiceScopeFactory scopes, ExperimentJobSettings settings, TimeProvider time, ILogger<ExperimentJobDispatcher> logger)
{
    /// <summary>The name this host claims jobs under.</summary>
    public const string Host = "xd-api";

    private Task? _running;

    /// <summary>Ends the jobs this host had running; call once before <see cref="RunAsync"/>.</summary>
    public async Task RecoverAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IExperimentJobStore>();
        var progress = scope.ServiceProvider.GetRequiredService<IExperimentJobProgress>();
        foreach (var id in await store.InterruptRunningAsync(Host, time.GetUtcNow(), cancellationToken))
        {
            ExperimentJobLogEvents.Interrupted(logger, id);
            await progress.FinishedAsync(new ExperimentJobFinished(id, ExperimentJobStates.Code(ExperimentJobState.Interrupted)), cancellationToken);
        }
    }

    /// <summary>The loop of the host: look for a queued job every poll interval until the host stops.</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        try
        {
            await GuardedAsync(RecoverAsync, stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                await GuardedAsync(async token => await TryStartAsync(token), stoppingToken);
                await Task.Delay(settings.PollInterval, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    /// <summary>For tests: claims and runs until nothing is queued and nothing is running.</summary>
    public async Task DrainAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var started = await TryStartAsync(cancellationToken);
            var going = _running;
            if (going is null && !started)
            {
                return;
            }

            if (going is not null)
            {
                await going;
            }
        }
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
            ExperimentJobLogEvents.DispatcherFailed(logger, ex);
        }
    }

    /// <summary>Claims the oldest queued job when none is running; returns whether one was started.</summary>
    private async Task<bool> TryStartAsync(CancellationToken cancellationToken)
    {
        if (_running is { IsCompleted: false })
        {
            return false;
        }

        ExperimentJob? job;
        await using (var scope = scopes.CreateAsyncScope())
        {
            job = await scope.ServiceProvider.GetRequiredService<IExperimentJobStore>().ClaimNextAsync(Host, time.GetUtcNow(), cancellationToken);
        }

        if (job is null)
        {
            _running = null;
            return false;
        }

        _running = Task.Run(() => ExecuteAsync(job, cancellationToken), CancellationToken.None);
        return true;
    }

    private async Task ExecuteAsync(ExperimentJob job, CancellationToken hostStop)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IExperimentJobExecutor>().ExecuteAsync(job, hostStop);
        }
        catch (OperationCanceledException) when (hostStop.IsCancellationRequested)
        {
            // shutting down: the job stays running and is marked interrupted at the next start
        }
#pragma warning disable CA1031 // one failed job must not stop the dispatcher; the executor has ended the job as far as the database allowed
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ExperimentJobLogEvents.Crashed(logger, job.Id, job.Stage ?? "claimed", ex);
        }
    }
}
