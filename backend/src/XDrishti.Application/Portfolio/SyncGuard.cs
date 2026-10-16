using XDrishti.Application.Abstractions.Portfolio;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio;

/// <summary>
/// Asks whether a run of the account that a write would race is running (ADR 0030): a holdings sync, a broker history run of trades or one of the ledger - whichever of them the write <see cref="SyncWaits">waits for</see>. The writes that would race it (a trade or bill import, a
/// rebuild of lots, an undo of a trade batch, a depository-charges import) refuse with <c>409 portfolio.sync_running</c> (<see cref="PortfolioErrors.SyncRunningWait"/>) while this is true. A run that hung is counted as running until the next sync starts (staleness is not looked at).
/// </summary>
internal sealed class SyncGuard(IHoldingSyncStore syncs, IBrokerSyncStore brokerRuns)
{
    public async ValueTask<bool> RunningAsync(Guid accountId, SyncWaits waitsFor, CancellationToken cancellationToken) =>
        (waitsFor.HasFlag(SyncWaits.Holdings) && await syncs.IsRunningAsync(accountId, cancellationToken))
        || (waitsFor.HasFlag(SyncWaits.Trades) && await brokerRuns.IsRunningAsync(accountId, BrokerSyncKind.Trades, cancellationToken))
        || (waitsFor.HasFlag(SyncWaits.Ledger) && await brokerRuns.IsRunningAsync(accountId, BrokerSyncKind.Ledger, cancellationToken));
}
