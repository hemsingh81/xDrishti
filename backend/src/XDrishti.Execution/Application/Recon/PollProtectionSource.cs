using XDrishti.Execution.Application.Halts;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application.Recon;

/// <summary>
/// The real protection snapshot (spec P12-06; P12-04 left a closed default): the order book and positions of the last good poll, the open intents and entries of our record and the strongest halt in force. It answers null, which nothing reads as protected, whenever it cannot vouch:
/// the streams are not alive (no poll within 15 s, or a gap fill is owed), no poll has completed, the halts or our record cannot be read. It is not the default and no host composes it; the host that does so decides when.
/// </summary>
public sealed class PollProtectionSource(PollService poll, StreamStatus status, IReconOurSource ours, HaltService halts, Func<DateOnly> session) : IProtectionSource
{
    public async Task<ProtectionSnapshot?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!status.StreamsAlive || poll.Latest is not { } latest || latest.Positions is null)
        {
            return null;
        }

        if (await halts.ActiveAsync(cancellationToken).ConfigureAwait(false) is not { } active)
        {
            return null;
        }

        List<ProtectedPosition> positions = [];
        List<WorkingEntry> entries = [];
        try
        {
            foreach (var account in await ours.AccountsAsync(cancellationToken).ConfigureAwait(false))
            {
                var view = await ours.LoadAsync(account, cancellationToken).ConfigureAwait(false);
                foreach (var intent in view.Intents)
                {
                    if (intent.State is ExecState.Filled or ExecState.Protected or ExecState.Managing or ExecState.Closing)
                    {
                        var net = view.Positions.FirstOrDefault(p => p.SecurityId == intent.SecurityId)?.NetQty ?? 0;
                        var broker = latest.Positions.Where(p => p.SecurityId == intent.SecurityId).Select(p => (int?)p.NetQty).FirstOrDefault() ?? 0;
                        positions.Add(new ProtectedPosition(account, intent.IntentId, intent.SecurityId, intent.State, net, intent.Stop, intent.Entry, null, null, broker, latest.At));
                    }
                    else if (intent.State == ExecState.Open && view.Legs.FirstOrDefault(l => l.IntentId == intent.IntentId && l.Kind == LegKind.Entry) is { } entry)
                    {
                        entries.Add(new WorkingEntry(account, intent.IntentId, entry.ClientOrderId));
                    }
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }

        HaltLevel? strongest = active.Count == 0 ? null : active.Max(h => h.Level);
        return new ProtectionSnapshot(latest.At, latest.Orders, positions, entries, strongest, session());
    }
}
