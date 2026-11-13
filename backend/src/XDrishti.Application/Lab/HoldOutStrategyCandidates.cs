using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>One strategy version that may take a block's fresh strategy look: the stored run to look from and when the version first froze its plan.</summary>
internal sealed record StrategyLookCandidate(LabRun Run, DateTimeOffset FrozenAt);

/// <summary>
/// The strategy versions that can still take the fresh strategy look at a block (HO-01 D4 and D10). For each version the NEWEST default-options full or robust succeeded run whose window is clear of the block decides (an older passing run never outweighs a newer failing one); the
/// version is a candidate when that run's development rows all pass (<see cref="HoldOutAutoGate"/>), its plan was frozen before the block's horizon and it has not looked at this block. Newest version first, ties broken by the earliest first freeze. Read only: one query for the runs
/// (no cap), then per version the freeze time and the look before the stored verdict, which is read only for a version that passed the cheap checks. It reads no bar.
/// </summary>
internal sealed class HoldOutStrategyCandidates(ILabRunStore runs, ILabResultStore results, IPlanFreezeStore freezes, IHoldOutStore store)
{
    public async ValueTask<IReadOnlyList<StrategyLookCandidate>> FindAsync(HoldOutBlock block, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(block);
        var firstBlock = block.PrevStart is null;
        var firstLookAt = firstBlock ? await store.FirstLookAtAsync(block.Start, cancellationToken) : null;
        var found = new List<StrategyLookCandidate>();
        var seen = new HashSet<(string, int)>();
        foreach (var run in await runs.ListSucceededFullTestsAsync(cancellationToken))
        {
            if (!LabSettings.RunsFullTest(run.Method) || seen.Contains((run.StrategyId, run.Version)))
            {
                continue;
            }

            // a what-if run never takes the version's look, and a run whose window reaches the block cannot pass, so neither decides
            if (LabRunOptions.Parse(run.RunOptions).KeyText(string.Equals(run.Method, LabSettings.RobustMethod, StringComparison.Ordinal)) is not null || run.To >= block.Start)
            {
                continue;
            }

            // the newest such run decides for the version, whatever its verdict
            seen.Add((run.StrategyId, run.Version));
            var frozenAt = await freezes.FirstFrozenAtAsync(run.StrategyId, run.Version, cancellationToken);
            if (frozenAt is not { } frozen
                || !HoldOutHorizon.IsPredated(HoldOutUnlocker.StrategyKind, firstBlock, block.Start, frozen, firstLookAt)
                || await store.GetUnlockAsync(run.StrategyId, run.Version, HoldOutUnlocker.StrategyKind, block.Start, cancellationToken) is not null)
            {
                continue;
            }

            var verdict = await results.GetAsync(run.Id, cancellationToken);
            if (verdict is null || !HoldOutAutoGate.AllPass(verdict, run.To, block.Start))
            {
                continue;
            }

            found.Add(new StrategyLookCandidate(run, frozen));
        }

        return [.. found.OrderByDescending(c => c.Run.Version).ThenBy(c => c.FrozenAt).ThenBy(c => c.Run.StrategyId, StringComparer.Ordinal)];
    }
}
