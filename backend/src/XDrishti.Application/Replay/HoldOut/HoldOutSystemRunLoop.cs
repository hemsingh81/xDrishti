using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Replay.Job;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>
/// The loop of the host (xd-api) that runs the claimed system replays (SR-03): every poll it runs, one at a time, each registration whose <c>system</c> look is claimed and that has no result. It waits while a queued replay runs, so two heavy replays never share the
/// memory budget. All logic is in <see cref="HoldOutSystemRunner"/>. Off when <c>Replay:RunWorker</c> is false (the tests drive the runner).
/// </summary>
public sealed class HoldOutSystemRunLoop(IServiceScopeFactory scopes, SystemReplayOptions options, ReplayRunner replays, TimeProvider time, ILogger<HoldOutSystemRunLoop> logger)
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(30);

    /// <summary>Looks every poll until the host stops.</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        if (!options.RunWorker)
        {
            return;
        }

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await TickAsync(stoppingToken);
                await Task.Delay(Poll, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down: a claim without a result is taken up again at the next start
        }
    }

    /// <summary>One pass.</summary>
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (replays.Busy)
            {
                return;
            }

            await using var scope = scopes.CreateAsyncScope();

            if (await scope.ServiceProvider.GetRequiredService<HoldOutSystemRunner>().RunPendingAsync(cancellationToken) is > 0 and var stored)
            {
                HoldOutSystemLog.Stored(logger, stored);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            HoldOutSystemLog.LoopFailed(logger, failure.GetType().Name);
        }
    }
}
