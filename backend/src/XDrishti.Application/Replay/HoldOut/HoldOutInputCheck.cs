using XDrishti.Application.Abstractions.Research;

namespace XDrishti.Application.Replay.HoldOut;

/// <summary>
/// Whether every stored input of a registration still resolves (SR-03 D4): each strategy version by its hash, the members of the scope, the statistics snapshot as of the block start and the cost sheet with the same rates hash. Only a missing input or a changed sheet is
/// drift; a changed live setting or engine version is not. Reads no bar: nothing of the block is touched before the claim.
/// </summary>
internal sealed class HoldOutInputCheck(IReplayFacts facts, IStrategyStore strategies, ICostSheetStore sheets)
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

        if ((await facts.UniverseAsync(config.Parameters, config.BlockStart, cancellationToken)).Members.Count == 0)
        {
            return "the scope has no members";
        }

        return (await facts.CellsAsync(config.Parameters, config.BlockStart, cancellationToken)).Cells.Count == 0 ? "there is no cell statistics snapshot as of the block start" : null;
    }
}
