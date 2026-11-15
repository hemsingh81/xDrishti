using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Research;

namespace XDrishti.Application.Replay.Sim;

/// <summary>Records a simulation (or the stretch a Continue adds) in the CF-06 trial ledger as a look of kind <c>system_replay</c>, one row per strategy version it uses, as SR-01 does (ADR 0272, 0293).</summary>
internal static class SimDaysLedger
{
    public static async ValueTask RecordAsync(ITestLedger ledger, Guid runId, string inputHash, IReadOnlyDictionary<string, IReadOnlyList<StoredVersion>> versions, DateOnly from, DateOnly to, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var look = $"sim-days|{runId:D}|{to:yyyy-MM-dd}";
        foreach (var v in versions.Values.SelectMany(l => l))
        {
            // the first half of the id belongs to the look and the second to the version, so the ledger counts looks, not rows
            var id = new Guid(Convert.FromHexString(ReplayJson.Sha256(look)[..16] + ReplayJson.Sha256(look + "|" + v.Hash)[..16]));
            await ledger.AppendAsync(new LedgerRow(id, inputHash, v.StrategyId, v.Version, v.Hash, null, "sim days", from, to, LedgerRow.SystemReplayKind, now, LedgerRow.SystemReplayKind, 0), cancellationToken);
        }
    }
}
