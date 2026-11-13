using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The model's one look at the locked hold-out, start to finish (CF-10e part 3, spec D5, ADR 0285). It claims the family's look at the block in force first (<see cref="ModelHoldOutLooks.ClaimAsync"/>: insert-only, burned even if the pass is abandoned or finds nothing, refused when the family or another model
/// already took the block's model slot, when the model was not registered before the block's horizon, or when no block is fixed), then runs the pass once, then stores the figures. The mode is <c>owner</c> (the endpoint has checked the fresh password) or <c>auto</c> (the seal step; the switch
/// <c>ml.holdout.auto_score</c> is read again here and the database refuses an automatic row while it is not ON); both record who and a setting snapshot. A look that was stored as re-used (another model took the slot between the checks) is never scored. A pass that cannot read its inputs
/// stores no figures: the look stays burned and the figures stay unavailable, never a pass.
/// </summary>
internal sealed class ModelHoldOutScorer(IModelRegistry registry, IHoldOutStore store, ModelHoldOutLooks looks, IModelHoldOutPass pass, IMlHoldOutScores scores, IMlSwitchSettings switches, MlInfluenceOptions options, TimeProvider time, ILogger<ModelHoldOutScorer> logger)
{
    /// <summary>Who an automatic score is recorded as.</summary>
    public const string AutoActor = "system:auto-score";

    internal const string Owner = "owner";
    internal const string Auto = "auto";

    private const int Draws = 2000;
    private const double Level = 0.9;
    private const int MinDays = 10;

    public async ValueTask<Result<MlHoldOutScoreView>> ScoreAsync(long modelId, string mode, string actor, CancellationToken cancellationToken)
    {
        var model = await registry.FindAsync(modelId, cancellationToken);
        if (model is null)
        {
            return Result.Failure<MlHoldOutScoreView>(ModelErrors.NotFound(modelId));
        }

        var state = (await switches.CurrentAsync(cancellationToken)).AutoHoldOutScore ?? new MlSwitchState(null, false);
        if (mode == Auto && !state.On)
        {
            return Result.Failure<MlHoldOutScoreView>(MlInfluenceErrors.InvalidLook("The automatic hold-out score is off (ml.holdout.auto_score)."));
        }

        var settingHash = MlInfluenceEvaluator.Hash($"model-holdout|{options.KeyText()}|{Draws}|{Level:R}|{MinDays}|mode={mode}|auto_score={(state.On ? "on" : "off")}|row={state.Id}");
        var claimed = await looks.ClaimAsync(model, mode, actor, settingHash, cancellationToken);
        if (claimed.IsFailure)
        {
            return Result.Failure<MlHoldOutScoreView>(claimed.Error);
        }

        var look = claimed.Value;
        if (look.ReusedWindow)
        {
            return Result.Failure<MlHoldOutScoreView>(MlInfluenceErrors.HoldOutReused(look.StrategyId));
        }

        var started = time.GetTimestamp();
        var block = look.BlockStart;
        var blocks = await store.ListBlocksAsync(cancellationToken);
        if (blocks.FirstOrDefault(b => b.Block.Start == block)?.Block is not { } hold)
        {
            return Result.Failure<MlHoldOutScoreView>(MlInfluenceErrors.HoldOutUnavailable("The block of the look is no longer stored."));
        }

        var ran = await pass.RunAsync(model, hold, cancellationToken);
        if (ran.IsFailure)
        {
            ModelLog.HoldOutPassFailed(logger, modelId, ran.Error.Code);
            return Result.Failure<MlHoldOutScoreView>(ran.Error);
        }

        var figures = ModelHoldOutFigures.Compute(ran.Value.Rows, ran.Value.BaseRate, Draws, Level, MinDays, $"{modelId}|{block:yyyy-MM-dd}|{ran.Value.InputHash}");
        if (figures.IsFailure)
        {
            ModelLog.HoldOutPassFailed(logger, modelId, figures.Error.Code);
            return Result.Failure<MlHoldOutScoreView>(figures.Error.Code == ModelHoldOutFigures.NoRowsCode ? MlInfluenceErrors.HoldOutNoRows() : figures.Error);
        }

        var f = figures.Value;
        var stored = new StoredMlHoldOutScore(modelId, block, look.StrategyId, f.Rows, f.Days, f.LogLossModel, f.LogLossBaseline, f.BaselineName, f.ProbabilityBetter, f.GainLow, f.GainHigh, f.PolicyEffectR, ran.Value.InputHash, settingHash, mode, actor, time.GetUtcNow());
        if (!await scores.InsertAsync(stored, cancellationToken))
        {
            return Result.Failure<MlHoldOutScoreView>(MlInfluenceErrors.HoldOutReused(look.StrategyId));
        }

        var millis = (long)time.GetElapsedTime(started).TotalMilliseconds;
        ModelLog.HoldOutScored(logger, modelId, mode, f.Rows, ran.Value.Dropped, millis);
        return Result.Success(new MlHoldOutScoreView(modelId, ModelHoldOutLooks.FamilyId(model.Model), block, mode, actor, f.Rows, f.Days, ran.Value.Dropped, f.LogLossModel, f.LogLossBaseline, f.BaselineName, f.ProbabilityBetter, f.GainLow, f.GainHigh, f.PolicyEffectR, settingHash, ran.Value.InputHash, stored.ScoredAt));
    }
}
