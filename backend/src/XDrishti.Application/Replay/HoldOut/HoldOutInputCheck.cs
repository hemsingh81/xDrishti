using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Market;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>
/// Whether every stored input of a registration still resolves (SR-03 D4), checked before anything is claimed: the cost sheet (present, still carrying its <c>checked</c> note, the same rates hash), each strategy version by its hash, the scope's members (some, and still within the
/// memory cap), and the cell-statistics snapshot the run freezes on (as-of before the block's first session; some, and created before the block began: a snapshot written on or after the start could hold the block's own trades; the daily refresh of the first session's evening is never used). Only a missing or changed input is drift; a changed live setting or engine version is not.
/// Reads no bar: nothing of the block is touched before the claim.
/// </summary>
internal sealed class HoldOutInputCheck(IReplayFacts facts, IStrategyStore strategies, ICostSheetStore sheets, SystemReplayOptions options)
{
    /// <summary>The first thing that is missing or changed, in words, or null when every input resolves.</summary>
    public async ValueTask<string?> DriftAsync(HoldOutRegistrationConfig config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Parameters.CostSheetId is not { } sheetId)
        {
            return "the registration names no cost sheet";
        }

        var found = await sheets.FindAsync(sheetId, cancellationToken);
        if (found.IsFailure)
        {
            return $"the cost sheet `{sheetId}` is missing or unreadable";
        }

        if (found.Value.Checked is null)
        {
            return $"the cost sheet `{sheetId}` is no longer checked (ISS-25)";
        }

        if (!string.Equals(found.Value.RatesSha256, config.CostSheetRatesHash, StringComparison.Ordinal))
        {
            return $"the cost sheet `{sheetId}` has a new content hash";
        }

        foreach (var (strategy, versions) in config.Versions)
        {
            var stored = await strategies.ListVersionsAsync(strategy, cancellationToken);
            foreach (var v in versions)
            {
                if (!stored.Any(s => s.Version == v.Version && string.Equals(s.Hash, v.Hash, StringComparison.Ordinal)))
                {
                    return $"strategy `{strategy}` version {v.Version} is missing";
                }
            }
        }

        var members = (await facts.UniverseAsync(config.Parameters, config.BlockStart, cancellationToken)).Members.Select(m => m.Id).Union(config.Parameters.Scope.InstrumentIds).Distinct().Count();
        if (members == 0)
        {
            return "the scope has no members";
        }

        if (members * options.BytesPerInstrument > options.MaxWindowMb * 1024L * 1024)
        {
            return $"the scope now reads {members} instruments, which is over the memory cap (Replay:MaxWindowMb)";
        }

        var cells = (await facts.CellsBeforeStartAsync(config.Parameters, cancellationToken)).Cells; // the same freeze the run uses: the snapshot as of before the block's first session
        if (cells.Count == 0)
        {
            return "there is no cell statistics snapshot as of the block start";
        }

        var begins = new DateTimeOffset(config.BlockStart.ToDateTime(TimeOnly.MinValue), IndiaTime.Offset);
        return cells.Any(c => c.Stored.CreatedAt >= begins) ? "the newest cell statistics snapshot was created on or after the block start" : null;
    }
}
