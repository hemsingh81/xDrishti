using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Application.Recon;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application.Halts;

/// <summary>
/// The book the drill judges flatness on, for one account (ADR 0174): the broker's order book and positions read now (GET only, through <see cref="IBrokerReads"/>) and our own record of that account. Null, which the drill reads as
/// <c>refused:no_snapshot</c>, whenever either side cannot be read: an unreadable book is never flat. Every non-zero broker position counts against the account (the sandbox credentials are one account; the stricter reading), and so does
/// every intent of ours that is not in a final state.
/// </summary>
public sealed class DrillBookSource(IBrokerReads reads, IReconOurSource ours, Guid accountId, TimeProvider clock) : IProtectionSource
{
    // The same final set as the API's open-intent count (ExecutionFactsReader): planned and closed are NOT final here, so they count against flatness (the stricter reading).
    private static readonly ExecState[] Final = [ExecState.Rejected, ExecState.Cancelled, ExecState.Failed, ExecState.Reconciled];

    public async Task<ProtectionSnapshot?> ReadAsync(CancellationToken cancellationToken)
    {
        var orders = await reads.GetOrderBookAsync(cancellationToken).ConfigureAwait(false);
        var positions = await reads.GetPositionsAsync(cancellationToken).ConfigureAwait(false);
        if (!orders.Readable || orders.Value is null || !positions.Readable || positions.Value is null)
        {
            return null;
        }

        OurAccountView view;
        try
        {
            view = await ours.LoadAsync(accountId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }

        var now = clock.GetUtcNow();
        List<ProtectedPosition> held = [.. positions.Value.Where(p => p.NetQty != 0).Select(p => new ProtectedPosition(accountId, 0, p.SecurityId, ExecState.Managing, p.NetQty, 0m, 0m, null, null, p.NetQty, now))];
        held.AddRange(view.Positions.Where(p => p.NetQty != 0).Select(p => new ProtectedPosition(accountId, 0, p.SecurityId, ExecState.Managing, p.NetQty, 0m, 0m, null, null, null, null)));
        List<WorkingEntry> working = [.. view.Intents.Where(i => !Final.Contains(i.State)).Select(i => new WorkingEntry(accountId, i.IntentId, string.Empty))];
        return new ProtectionSnapshot(now, orders.Value, held, working, null, DateOnly.FromDateTime(now.UtcDateTime));
    }
}
