using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Application.Recon;

namespace XDrishti.Execution.Application.Halts;

/// <summary>
/// Runs one kill-switch drill for an account (spec P12-05 criterion 10): a <see cref="DrillService"/> over the real halt record and <see cref="DrillBookSource"/>. Composed only where <see cref="IBrokerReads"/> exists, which is the sandbox send
/// composition (environment sandbox AND <c>Execution:SendEnabled</c>); never in live or none. The drill builds its own sender with a transport that refuses everything, so no order can leave.
/// </summary>
public sealed class DrillRunner(HaltService halts, IHaltStore store, IBrokerReads reads, IReconOurSource ours, IDrillStore drills, TimeProvider clock)
{
    public Task<DrillResult> RunAsync(Guid accountId, CancellationToken cancellationToken) =>
        new DrillService(halts, store, new DrillBookSource(reads, ours, accountId, clock), drills, clock).RunAsync(accountId, cancellationToken);
}
