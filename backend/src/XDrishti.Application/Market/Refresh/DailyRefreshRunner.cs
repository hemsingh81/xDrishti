using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Market;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Market.Refresh;

/// <summary>
/// The daily automatic refresh: waits for the configured India time (by <see cref="TimeProvider"/>, in short slices that re-read the clock, so a
/// sleeping or adjusted machine wakes up on time) and then starts a refresh through the same command the endpoint uses. When it starts and the last
/// finished refresh is older than the most recent scheduled time (the application was down then), it refreshes at once; an unchanged file costs one
/// conditional request. The API's hosted service only calls <see cref="RunAsync"/>.
/// </summary>
public sealed class DailyRefreshRunner(
    IServiceScopeFactory scopes,
    IInstrumentRefreshSettings settings,
    TimeProvider clock,
    ILogger<DailyRefreshRunner> logger)
{
    /// <summary>Recorded as the requester of automatic runs.</summary>
    public const string Requester = "system:daily-refresh";

    /// <summary>The longest single wait; the clock is read again after each.</summary>
    public static readonly TimeSpan MaxSlice = TimeSpan.FromMinutes(1);

    /// <summary>Runs until cancelled, or returns at once when the automatic refresh is switched off.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!settings.AutoRefreshEnabled)
        {
            MarketLog.AutoRefreshOff(logger);
            return;
        }

        try
        {
            await CatchUpAsync(cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                var next = RefreshSchedule.NextRun(clock.GetUtcNow(), settings.AutoRefreshTimeIst);
                MarketLog.AutoRefreshScheduled(logger, next);
                while (clock.GetUtcNow() < next)
                {
                    await Task.Delay(TimeSpan.FromTicks(Math.Min((next - clock.GetUtcNow()).Ticks, MaxSlice.Ticks)), clock, cancellationToken);
                }

                await StartAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    /// <summary>Starts a refresh now when none has finished since the most recent scheduled time (or none ever ran).</summary>
    private async Task CatchUpAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var last = await scope.ServiceProvider.GetRequiredService<IRefreshRunStore>().GetLastFinishedAsync(cancellationToken);
            var due = RefreshSchedule.PreviousRun(clock.GetUtcNow(), settings.AutoRefreshTimeIst);
            if (last is not null && last.StartedAt >= due)
            {
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // if the check fails, the schedule still runs; the next scheduled time catches up
        catch (Exception ex)
#pragma warning restore CA1031
        {
            MarketLog.AutoRefreshCrashed(logger, ex);
            return;
        }

        MarketLog.AutoRefreshCatchUp(logger);
        await StartAsync(cancellationToken);
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<StartRefreshCommand, RefreshStarted>>();
            var result = await handler.HandleAsync(new StartRefreshCommand(RefreshTrigger.Daily, false, Requester), cancellationToken);
            if (result.IsFailure)
            {
                MarketLog.AutoRefreshSkipped(logger);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // a failed start must not end the daily loop; tomorrow's run is still scheduled
        catch (Exception ex)
#pragma warning restore CA1031
        {
            MarketLog.AutoRefreshCrashed(logger, ex);
        }
    }
}
