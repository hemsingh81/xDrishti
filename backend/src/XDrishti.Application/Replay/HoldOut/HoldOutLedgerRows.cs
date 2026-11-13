using XDrishti.Application.Abstractions.Lab;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>
/// The trial-ledger rows of a system replay over a hold-out block (SR-03): one <c>system_replay</c> row per strategy version the registration uses, keyed by the look (the run key is the SHA-256 of the look id), no figure. The ids are deterministic and the ledger writes a row once,
/// so the claim and the runner both append them: the run never reads the block without its rows, and the second append writes nothing.
/// </summary>
internal static class HoldOutLedgerRows
{
    public static async ValueTask AppendAsync(ITestLedger ledger, Guid registrationId, HoldOutRegistrationConfig config, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(config);
        var runKey = ReplayJson.Sha256(HoldOutRegistrationIds.LookId(registrationId).ToString("N"));
        foreach (var v in config.Versions.Values.SelectMany(l => l))
        {
            var rowId = new Guid(Convert.FromHexString(ReplayJson.Sha256($"system-replay|{runKey}")[..16] + ReplayJson.Sha256($"system-replay|{runKey}|{v.Hash}")[..16]));
            await ledger.AppendAsync(new LedgerRow(rowId, runKey, v.StrategyId, v.Version, v.Hash, null, "system replay", config.BlockStart, config.BlockEnd, LedgerRow.SystemReplayKind, now, LedgerRow.SystemReplayKind, 0), cancellationToken);
        }
    }
}
