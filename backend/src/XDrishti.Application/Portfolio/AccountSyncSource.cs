using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Platform;
using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Application.Portfolio.Refresh;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>The per-account holdings sync for the Services screen's read-only row: the latest run over the accounts that count and the earliest next automatic run of the accounts whose refresh is on (<see cref="PortfolioRefreshSchedule"/>).</summary>
internal sealed class AccountSyncSource(IAccountStore accounts, IHoldingSyncStore syncs, TimeProvider clock) : IAccountSyncSource
{
    public async ValueTask<AccountSyncState> GetAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var on = (await accounts.ListActiveAsync(cancellationToken)).Where(a => a.Portfolio is { Enabled: true } && a.Portfolio.Refresh.IsOn).ToList();
        var activity = on.Count == 0 ? new Dictionary<Guid, SyncActivity>() : (await syncs.ActivityAsync([.. on.Select(a => a.Id)], cancellationToken)).ToDictionary(a => a.Key, a => a.Value);
        DateTimeOffset? next = on.Count == 0
            ? null
            : on.Select(a => PortfolioRefreshSchedule.NextRun(now, a.Portfolio.Refresh, activity.GetValueOrDefault(a.Id) ?? new SyncActivity(null))).Where(t => t is not null).Min();
        var last = await syncs.GetLastAsync(null, cancellationToken);
        return new AccountSyncState(on.Count > 0, last is null ? null : ToRun(last), next);
    }

    private static AccountSyncRun ToRun(HoldingSync sync) =>
        new(sync.Id, sync.Trigger == SyncTrigger.Auto ? "scheduled" : "manual", sync.StartedAt, sync.FinishedAt, sync.Status.ToString().ToLowerInvariant(), sync.Error);
}
