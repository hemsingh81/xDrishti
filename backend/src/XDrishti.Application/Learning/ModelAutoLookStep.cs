using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Lab;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Learning;

/// <summary>
/// The model look of the evening seal service (CF-10e part 3, HO-01 D10, ADR 0282 and 0285), seal step 30: with <c>ml.holdout.auto_score</c> ON (the seed default; no row reads OFF) and the block in force sealed (never filling, never block 1, whose first look stays the owner's act and sets the horizon), it scores the
/// pre-dated Eligible champion once, recorded as <c>auto</c> with the setting snapshot. The model is chosen by the fixed rule of <see cref="ModelHoldOutCandidates"/>, never by results. A read that fails throws and the service records the step as failed; the block stays as it was.
/// </summary>
internal sealed class ModelAutoLookStep(HoldOutEpochs epochs, ModelHoldOutCandidates candidates, ModelHoldOutScorer scorer, IMlSwitchSettings switches) : IHoldOutSealStep
{
    public string Name => "model look";

    public int Order => 30;

    public async ValueTask<string> RunAsync(CancellationToken cancellationToken)
    {
        if (!((await switches.CurrentAsync(cancellationToken)).AutoHoldOutScore?.On ?? false))
        {
            return "auto score off";
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

        var found = await candidates.FindAsync(current.Block, cancellationToken);
        if (found.Count == 0)
        {
            return "no candidate";
        }

        // one model per block: the champion is the only model of the rule, and the slot holds one fresh look
        var model = found[0];
        var scored = await scorer.ScoreAsync(model.Id, ModelHoldOutScorer.Auto, ModelHoldOutScorer.AutoActor, cancellationToken);
        return scored.IsSuccess ? $"scored model {model.Id}" : $"model {model.Id} not scored ({scored.Error.Code})";
    }
}
