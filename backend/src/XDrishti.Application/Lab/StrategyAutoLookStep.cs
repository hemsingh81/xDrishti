using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>
/// The strategy auto look of the seal service (HO-01 D10): with <c>Confidence:HoldOut:AutoUnlock</c> on and the block in force sealed with its strategy slot free, queues the existing <c>holdout</c> Lab job, recorded as <c>auto</c>, for the best candidate
/// (<see cref="HoldOutStrategyCandidates"/>). Never at block 1 (its look stays the owner's act) and never at a filling block. The job runs the look as the Lab dispatcher does for the owner's; this step only claims it.
/// </summary>
internal sealed class StrategyAutoLookStep(HoldOutEpochs epochs, HoldOutStrategyCandidates candidates, HoldOutUnlocker unlocker, IHoldOutStore store, ILabRunStore runs, HoldOutOptions options, TimeProvider time, ILogger<StrategyAutoLookStep> logger) : IHoldOutSealStep
{
    public const string Actor = "system:auto-unlock";

    public string Name => "strategy look";

    public int Order => 20;

    public async ValueTask<string> RunAsync(CancellationToken cancellationToken)
    {
        if (!options.AutoUnlock)
        {
            return "auto look off";
        }

        if (await epochs.InForceAsync(cancellationToken) is not { } current)
        {
            return "no block";
        }

        if (current.State is not (HoldOutBlockState.Sealed or HoldOutBlockState.Looked))
        {
            return "block " + HoldOutChain.Word(current.State);
        }

        if (current.Block.PrevStart is null)
        {
            return "block 1 is the owner's";
        }

        if (await store.HasFreshLookAsync(current.Block.Start, HoldOutUnlocker.StrategyKind, cancellationToken))
        {
            return "slot used";
        }

        foreach (var candidate in await candidates.FindAsync(current.Block, cancellationToken))
        {
            // the same cheap checks as the owner's unlock; a refusal moves to the next candidate
            var plan = await unlocker.CheckAsync(candidate.Run.Id, cancellationToken);
            if (plan.IsFailure)
            {
                continue;
            }

            var run = plan.Value.Run;
            var labels = new[] { HoldOutJob.Label(run.Id, Actor), "Hold-out look (auto): the frozen plan of this run's version, run once over the block in force; recorded as auto." };
            var queued = await runs.InsertOrGetAsync(
                new NewLabRun(Guid.NewGuid(), HoldOutJob.KeyOf(run.Id, plan.Value.Block.Start), run.StrategyId, run.Version, run.StrategyHash, run.Universe, run.From, run.To, HoldOutJob.Method, HoldOutJob.ConfigHash, run.EngineVersion, labels, time.GetUtcNow()),
                cancellationToken);
            if (!queued.Created)
            {
                // a look of this run at this block is queued, running or done already: this evening claims nothing new for it, and the next candidate may still be claimed
                LabRunLog.HoldOutLookAlreadyQueued(logger, run.Id);
                continue;
            }

            return $"look queued for {run.StrategyId} v{run.Version}";
        }

        return "no candidate";
    }
}
