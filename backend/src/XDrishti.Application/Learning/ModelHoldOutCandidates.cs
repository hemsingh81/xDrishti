using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The models that may still take the block's fresh model look by themselves (CF-10e part 3, HO-01 D4 and D10, ADR 0285): the current champion whose development verdict is Eligible, that kept a hold-out ending its training before the block, that was registered before the block's horizon
/// (<see cref="HoldOutHorizon"/>: only a pre-dated model is ever chosen) and whose family has not looked at the block (or whose own claimed look has no figures yet, which it may finish). Nothing is chosen by its results: the rule is fixed. Read only; it reads no bar.
/// </summary>
internal sealed class ModelHoldOutCandidates(IModelRegistry registry, IHoldOutStore store, IMlHoldOutScores scores)
{
    private const int Limit = 50;

    public async ValueTask<IReadOnlyList<StoredModel>> FindAsync(HoldOutBlock block, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(block);
        var firstBlock = block.PrevStart is null;
        var firstLookAt = firstBlock ? await store.FirstLookAtAsync(block.Start, cancellationToken) : null;
        var found = new List<StoredModel>();
        foreach (var champion in await registry.ListAsync(ModelRole.Champion, Limit, cancellationToken))
        {
            var model = champion.Model;
            if (model.Verdict != ValidationOutcome.Eligible
                || model.HoldOutStart is not { } kept || kept > block.Start
                || !HoldOutHorizon.IsPredated(ModelHoldOutLooks.ModelKind, firstBlock, block.Start, model.CreatedAt, firstLookAt))
            {
                continue;
            }

            // a look the model claimed itself and has no figures for is still its to finish (ADR 0285, amendment); any other look at the block is taken
            if (await store.GetUnlockAsync(ModelHoldOutLooks.LookKey(model, block.Start), ModelHoldOutLooks.FamilyVersion, ModelHoldOutLooks.ModelKind, block.Start, cancellationToken) is { } look
                && (look.RunId != ModelHoldOutLooks.LookId(model) || look.ReusedWindow || await scores.FindAsync(champion.Id, block.Start, cancellationToken) is not null))
            {
                continue;
            }

            found.Add(champion);
        }

        return found;
    }
}
