using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Heal;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Refresh;

/// <summary>
/// The automatic holdings refresh: first ends the syncs an earlier process left "running", then once a minute (by <see cref="TimeProvider"/>) looks
/// at every active account with the Portfolio role whose refresh is on, and runs the same sync command as "Sync now" for those that are due
/// (<see cref="PortfolioRefreshSchedule"/>). Accounts are handled one after the other, so runs never overlap; the database also allows one running
/// sync per account. The API's hosted service only calls <see cref="RunAsync"/>.
/// </summary>
public sealed class PortfolioRefreshRunner(
    IServiceScopeFactory scopes,
    IPortfolioRefreshSettings settings,
    IApplicationInfo application,
    TimeProvider clock,
    ILogger<PortfolioRefreshRunner> logger)
{
    /// <summary>Recorded as the requester of automatic runs.</summary>
    public const string Requester = "system:portfolio-refresh";

    /// <summary>How often the accounts are looked at.</summary>
    public static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

    /// <summary>Runs until cancelled, or returns at once (after the clean-up) when the automatic refresh is switched off in the configuration.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RecoverAsync(cancellationToken);
            if (!settings.AutoRefreshEnabled)
            {
                PortfolioLog.AutoRefreshOff(logger);
                return;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                await TickAsync(cancellationToken);
                await Task.Delay(Tick, clock, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    /// <summary>
    /// One look at the accounts: runs the sync of every account that is due, then lets <see cref="PortfolioHealer"/> retry the syncs that failed for a reason that may pass and catch up what a renewed Dhan login makes possible again (ADR 0027). Public so that
    /// tests (and nothing else) can drive the clock.
    /// </summary>
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        await RefreshDueAsync(cancellationToken);
        await HealAsync(cancellationToken);
    }

    private async Task HealAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            _ = await scope.ServiceProvider.GetRequiredService<PortfolioHealer>().TickAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // a failed look must not end the loop; the next minute looks again
        catch (Exception ex)
#pragma warning restore CA1031
        {
            HealLog.TickCrashed(logger, ex);
        }
    }

    private async Task RefreshDueAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<(Guid Id, string Slug, PortfolioRefresh Refresh)> candidates;
        IReadOnlyDictionary<Guid, SyncActivity> activity;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var accounts = await scope.ServiceProvider.GetRequiredService<IAccountStore>().ListActiveAsync(cancellationToken);
            candidates = [.. accounts.Where(a => a.Portfolio is { Enabled: true } && a.Portfolio.Refresh.IsOn).Select(a => (a.Id, a.Slug, a.Portfolio.Refresh))];
            if (candidates.Count == 0)
            {
                return;
            }

            activity = await scope.ServiceProvider.GetRequiredService<IHoldingSyncStore>().ActivityAsync([.. candidates.Select(c => c.Id)], cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // a failed look must not end the loop; the next minute looks again
        catch (Exception ex)
#pragma warning restore CA1031
        {
            PortfolioLog.AutoRefreshTickCrashed(logger, ex);
            return;
        }

        foreach (var (id, slug, refresh) in candidates)
        {
            var now = clock.GetUtcNow();
            if (!PortfolioRefreshSchedule.IsDue(now, refresh, activity.GetValueOrDefault(id) ?? new SyncActivity(null)))
            {
                continue;
            }

            await SyncAsync(id, slug, cancellationToken);
        }
    }

    private async Task SyncAsync(Guid accountId, string slug, CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<SyncHoldingsCommand, SyncRunView>>();

            // A refusal (not connected, a run in progress, Dhan unreachable) is already recorded or logged by the handler.
            _ = await handler.HandleAsync(new SyncHoldingsCommand(accountId, SyncTrigger.Auto, Requester), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // one failed account must not stop the others or the loop
        catch (Exception ex)
#pragma warning restore CA1031
        {
            PortfolioLog.AutoRefreshCrashed(logger, slug, ex);
        }
    }

    private async Task RecoverAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RecoverInterruptedSyncsCommand>>();
            await handler.HandleAsync(new RecoverInterruptedSyncsCommand(application.StartedAt), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // housekeeping: the refresh must still run (stale runs also expire after ten minutes)
        catch (Exception ex)
#pragma warning restore CA1031
        {
            PortfolioLog.RecoveryFailed(logger, ex);
        }
    }
}
