using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The model's one look at the locked hold-out, start to finish (CF-10e part 3, spec D5, ADR 0285). In this order, so that an input that is missing refuses the act before anything is burned: the block in force must be sealed (a filling block is refused <c>lab.holdout_filling</c>); a look this model already
/// claimed and has no figures for is resumed (see below), any other look of the family at the block is refused <c>learning.holdout_reused</c>; every input of the pass is prepared (no bar of the block is read; a failure is <c>learning.holdout_unavailable</c> and nothing is written); the look is claimed
/// (<see cref="ModelHoldOutLooks.ClaimAsync"/>: insert-only, refused when the model was not registered before the block's horizon or kept a hold-out that reaches into the block); the pass runs once; the figures are stored. The mode is <c>owner</c> (the endpoint has checked the fresh password) or <c>auto</c> (the
/// seal step; the switch <c>ml.holdout.auto_score</c> is read again before the claim and before the insert, and the database refuses an automatic row while it is not ON); both record who and a setting snapshot.
/// <b>Resume (ADR 0285, amendment):</b> a pass that fails after the claim (a database that goes away, a stop of the host) stores no figure, so it is not a second look: the same model may finish its own claimed look (the unlock is its own, fresh, has no score row, and the block is still in force and sealed). A look is never given to another model, and a
/// finished look is never redone. A transient failure after the claim answers <c>learning.holdout_interrupted</c> (409) instead of an error of the server.
/// </summary>
internal sealed class ModelHoldOutScorer(IModelRegistry registry, IHoldOutStore store, HoldOutEpochs epochs, ModelHoldOutLooks looks, IModelHoldOutPass pass, IMlHoldOutScores scores, IMlSwitchSettings switches, MlInfluenceOptions options, TimeProvider time, ILogger<ModelHoldOutScorer> logger)
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

        var state = await AutoScoreAsync(cancellationToken);
        if (mode == Auto && !state.On)
        {
            return Result.Failure<MlHoldOutScoreView>(MlInfluenceErrors.InvalidLook("The automatic hold-out score is off (ml.holdout.auto_score)."));
        }

        if (await epochs.InForceAsync(cancellationToken) is not { } current)
        {
            return Result.Failure<MlHoldOutScoreView>(MlInfluenceErrors.HoldOutUnavailable("No locked hold-out block is fixed yet."));
        }

        if (current.State == HoldOutBlockState.Filling)
        {
            return Result.Failure<MlHoldOutScoreView>(LabErrors.HoldOutFilling(current.Block.Start, current.End));
        }

        var block = current.Block;
        var key = ModelHoldOutLooks.LookKey(model.Model, block.Start);
        var existing = await store.GetUnlockAsync(key, ModelHoldOutLooks.FamilyVersion, ModelHoldOutLooks.ModelKind, block.Start, cancellationToken);
        if (existing is not null && (existing.RunId != ModelHoldOutLooks.LookId(model.Model) || existing.ReusedWindow || await scores.FindAsync(modelId, block.Start, cancellationToken) is not null))
        {
            return Result.Failure<MlHoldOutScoreView>(MlInfluenceErrors.HoldOutReused(key));
        }

        var prepared = await pass.PrepareAsync(model, block.Start, cancellationToken);
        if (prepared.IsFailure)
        {
            ModelLog.HoldOutPassFailed(logger, modelId, prepared.Error.Code);
            return Result.Failure<MlHoldOutScoreView>(prepared.Error);
        }

        var settingHash = MlInfluenceEvaluator.Hash($"model-holdout|{options.KeyText()}|{Draws}|{Level:R}|{MinDays}|mode={mode}|auto_score={(state.On ? "on" : "off")}|row={state.Id}");
        var look = existing;
        if (look is null)
        {
            var claimed = await looks.ClaimAsync(model, mode, actor, settingHash, cancellationToken);
            if (claimed.IsFailure)
            {
                return Result.Failure<MlHoldOutScoreView>(claimed.Error);
            }

            look = claimed.Value;
            if (look.ReusedWindow)
            {
                return Result.Failure<MlHoldOutScoreView>(MlInfluenceErrors.HoldOutReused(look.StrategyId));
            }
        }

        var started = time.GetTimestamp();
        Result<ModelHoldOutRows> ran;
        try
        {
            ran = await pass.RunAsync(prepared.Value, block, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException && TransientFailure.Is(failure))
        {
            ModelLog.HoldOutPassFailed(logger, modelId, MlInfluenceErrors.HoldOutInterruptedCode);
            return Result.Failure<MlHoldOutScoreView>(MlInfluenceErrors.HoldOutInterrupted());
        }

        if (ran.IsFailure)
        {
            ModelLog.HoldOutPassFailed(logger, modelId, ran.Error.Code);
            return Result.Failure<MlHoldOutScoreView>(ran.Error);
        }

        var figures = ModelHoldOutFigures.Compute(ran.Value.Rows, ran.Value.BaseRate, Draws, Level, MinDays, $"{modelId}|{block.Start:yyyy-MM-dd}|{ran.Value.InputHash}");
        if (figures.IsFailure)
        {
            ModelLog.HoldOutPassFailed(logger, modelId, figures.Error.Code);
            return Result.Failure<MlHoldOutScoreView>(figures.Error.Code == ModelHoldOutFigures.NoRowsCode ? MlInfluenceErrors.HoldOutNoRows() : figures.Error);
        }

        // the setting may have been turned OFF while the pass ran: the database would refuse the automatic row, so say so here; the claimed look stays resumable (the owner's score finishes it)
        if (mode == Auto && !(await AutoScoreAsync(cancellationToken)).On)
        {
            return Result.Failure<MlHoldOutScoreView>(MlInfluenceErrors.InvalidLook("The automatic hold-out score was turned off while the pass ran; the claimed look stays open for the owner to finish."));
        }

        var f = figures.Value;
        var stored = new StoredMlHoldOutScore(modelId, block.Start, look.StrategyId, f.Rows, f.Days, f.LogLossModel, f.LogLossBaseline, f.BaselineName, f.ProbabilityBetter, f.GainLow, f.GainHigh, f.PolicyEffectR, ran.Value.InputHash, settingHash, mode, actor, time.GetUtcNow(),
            ran.Value.Dropped, block.HistoryLast, ModelHoldOutLooks.LookId(model.Model), state.Id);
        if (!await scores.InsertAsync(stored, cancellationToken))
        {
            return Result.Failure<MlHoldOutScoreView>(MlInfluenceErrors.HoldOutReused(look.StrategyId));
        }

        var millis = (long)time.GetElapsedTime(started).TotalMilliseconds;
        ModelLog.HoldOutScored(logger, modelId, mode, f.Rows, ran.Value.Dropped, millis);
        return Result.Success(new MlHoldOutScoreView(modelId, ModelHoldOutLooks.FamilyId(model.Model), block.Start, mode, actor, f.Rows, f.Days, ran.Value.Dropped, f.LogLossModel, f.LogLossBaseline, f.BaselineName, f.ProbabilityBetter, f.GainLow, f.GainHigh, f.PolicyEffectR, settingHash, ran.Value.InputHash, stored.ScoredAt));
    }

    private async ValueTask<MlSwitchState> AutoScoreAsync(CancellationToken cancellationToken) =>
        (await switches.CurrentAsync(cancellationToken)).AutoHoldOutScore ?? new MlSwitchState(null, false);
}
