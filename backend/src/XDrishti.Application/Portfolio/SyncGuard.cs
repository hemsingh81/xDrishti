using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>
/// Asks whether anything of the account that writes trades, ledger entries or lots is running: a holdings sync, or a broker history run (trades or ledger). The writes that would race it (a trade or cost import, a rebuild of lots, an undo of a trade batch) refuse with
/// <c>409 portfolio.sync_running</c> while this is true. A run that hung is counted as running until the next sync starts (staleness is not looked at).
/// </summary>
internal sealed class SyncGuard(IHoldingSyncStore syncs, IBrokerSyncStore brokerRuns)
{
    public async ValueTask<bool> RunningAsync(Guid accountId, CancellationToken cancellationToken) =>
        await syncs.IsRunningAsync(accountId, cancellationToken) || await brokerRuns.IsRunningAsync(accountId, cancellationToken);
}
