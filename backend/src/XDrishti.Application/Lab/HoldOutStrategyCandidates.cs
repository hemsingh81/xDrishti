using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>One strategy version that may take a block's fresh strategy look: the stored run to look from and when the version first froze its plan.</summary>
internal sealed record StrategyLookCandidate(LabRun Run, DateTimeOffset FrozenAt);

/// <summary>
/// The strategy versions that can still take the fresh strategy look at a block (HO-01 D4 and D10): a version frozen before the block's horizon, not yet looked at this block, with a stored default-options full or robust Lab run that succeeded and whose development rows all pass
/// (<see cref="HoldOutAutoGate"/>, the window clear of the block). Newest version first, ties broken by the earliest first freeze. Read only: it reads run headers, stored verdicts and freeze times, never a bar.
/// </summary>
internal sealed class HoldOutStrategyCandidates(ILabRunStore runs, ILabResultStore results, IPlanFreezeStore freezes, IHoldOutStore store)
{
    private const int RunLimit = 500;

    public async ValueTask<IReadOnlyList<StrategyLookCandidate>> FindAsync(HoldOutBlock block, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(block);
        var firstBlock = block.PrevStart is null;
        var firstLookAt = firstBlock ? await store.FirstLookAtAsync(block.Start, cancellationToken) : null;
        var found = new List<StrategyLookCandidate>();
        var seen = new HashSet<(string, int)>();
        foreach (var run in await runs.ListAsync(null, LabRunState.Succeeded, RunLimit, cancellationToken))
        {
            if (!LabSettings.RunsFullTest(run.Method) || seen.Contains((run.StrategyId, run.Version)))
            {
                continue;
            }

            // a what-if run never takes the version's look
            if (LabRunOptions.Parse(run.RunOptions).KeyText(string.Equals(run.Method, LabSettings.RobustMethod, StringComparison.Ordinal)) is not null)
            {
                continue;
            }

            var verdict = await results.GetAsync(run.Id, cancellationToken);
            if (verdict is null || !HoldOutAutoGate.AllPass(verdict, run.To, block.Start))
            {
                continue;
            }

            seen.Add((run.StrategyId, run.Version));
            var frozenAt = await freezes.FirstFrozenAtAsync(run.StrategyId, run.Version, cancellationToken);
            if (frozenAt is not { } frozen
                || !HoldOutHorizon.IsPredated(HoldOutUnlocker.StrategyKind, firstBlock, block.Start, frozen, firstLookAt)
                || await store.GetUnlockAsync(run.StrategyId, run.Version, HoldOutUnlocker.StrategyKind, block.Start, cancellationToken) is not null)
            {
                continue;
            }

            found.Add(new StrategyLookCandidate(run, frozen));
        }

        return [.. found.OrderByDescending(c => c.Run.Version).ThenBy(c => c.FrozenAt).ThenBy(c => c.Run.StrategyId, StringComparer.Ordinal)];
    }
}
