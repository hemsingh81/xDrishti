using System.Security.Cryptography;
using System.Text;
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
/// The model's one look at the locked hold-out, start to finish (CF-10e part 3, spec D5, ADR 0285). In this order, so that an input that is missing refuses the act before anything is burned: the block in force must be sealed (a filling block is refused <c>lab.holdout_filling</c>); a look this model's file already claimed and has no
/// row for is resumed, any other look of the family at the block is refused <c>learning.holdout_reused</c>; every input of the pass is prepared (no bar of the block is read; a failure is <c>learning.holdout_unavailable</c> and nothing is written); the look is claimed (<see cref="ModelHoldOutLooks.ClaimAsync"/>); the pass
/// runs once over every planned strategy version; one row is stored. The mode is <c>owner</c> (the endpoint has checked the fresh password) or <c>auto</c> (the seal step; the switch <c>ml.holdout.auto_score</c> is read again before the claim and before the insert).
/// <para>
/// <b>How a look ends (ADR 0285, amendments).</b> Only a failure that goes away on its own leaves the look open to be finished: a database or network failure (found by walking the inner exceptions), a stop of the host, a version that could not be replayed, the setting turned off during the pass. The same file finishes
/// its own look (not a second one: no figure was ever stored), for at most <see cref="MaxResumeDays"/> days after the claim. Everything else ends the look with a <b>terminal row</b> and no figures: <c>no_rows</c> (the block gave nothing to score), or <c>failed</c> with a code (a deterministic error, a refusal by the database, or the
/// resume window elapsed). A terminal row means the model slot reads Used, the gate reads the figures as unavailable (never a pass) and the release can go on. The look belongs to the file (the look id is a hash of it) and so does its row: a second registry row with the same file never gets a second set of figures.
/// </para>
/// </summary>
internal sealed class ModelHoldOutScorer(IModelRegistry registry, IHoldOutStore store, HoldOutEpochs epochs, ModelHoldOutLooks looks, IModelHoldOutPass pass, IMlHoldOutScores scores, IMlSwitchSettings switches, MlInfluenceOptions options, TimeProvider time, ILogger<ModelHoldOutScorer> logger)
{
    /// <summary>Who an automatic score is recorded as.</summary>
    public const string AutoActor = "system:auto-score";

    /// <summary>How long after the claim a transient failure may still be resumed (about five evenings of the seal service); after it the look ends as failed.</summary>
    public const int MaxResumeDays = 7;

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
        var lookId = ModelHoldOutLooks.LookId(model.Model);
        var settingHash = MlInfluenceEvaluator.Hash($"model-holdout|{options.KeyText()}|{Draws}|{Level:R}|{MinDays}|mode={mode}|auto_score={(state.On ? "on" : "off")}|row={state.Id}");
        var existing = await store.GetUnlockAsync(key, ModelHoldOutLooks.FamilyVersion, ModelHoldOutLooks.ModelKind, block.Start, cancellationToken);
        var resumed = false;
        if (existing is not null)
        {
            // the look is the file's: another look, a re-used one, or one that has ended (scored or terminal, found by look and not by registry row) is refused
            if (existing.RunId != lookId || existing.ReusedWindow || await scores.FindByLookAsync(key, block.Start, lookId, cancellationToken) is not null)
            {
                return Result.Failure<MlHoldOutScoreView>(MlInfluenceErrors.HoldOutReused(key));
            }

            // the same conditions as at the claim: a model that kept a hold-out reaching into the block, or was registered on or after the block's horizon, never finishes a look
            var firstBlock = block.PrevStart is null;
            var firstLookAt = firstBlock ? await store.FirstLookAtAsync(block.Start, cancellationToken) : null;
            if (model.Model.HoldOutStart is not { } kept || kept > block.Start || !HoldOutHorizon.IsPredated(ModelHoldOutLooks.ModelKind, firstBlock, block.Start, model.Model.CreatedAt, firstLookAt))
            {
                return Result.Failure<MlHoldOutScoreView>(MlInfluenceErrors.HoldOutUnavailable("The model cannot finish this look: its kept hold-out reaches into the block, or it was registered on or after the block's horizon."));
            }

            if (time.GetUtcNow() > existing.UnlockedAt.AddDays(MaxResumeDays))
            {
                await StoreTerminalAsync(model, block, key, MlHoldOutOutcome.Failed, "resume_window_elapsed", mode, actor, settingHash, state, 0, 0, 0, cancellationToken);
                return Result.Failure<MlHoldOutScoreView>(MlInfluenceErrors.HoldOutFailed("resume_window_elapsed"));
            }

            resumed = true;
        }

        var prepared = await pass.PrepareAsync(model, block.Start, cancellationToken);
        if (prepared.IsFailure)
        {
            ModelLog.HoldOutPassFailed(logger, modelId, prepared.Error.Code);
            return Result.Failure<MlHoldOutScoreView>(prepared.Error);
        }

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
        else if (resumed)
        {
            ModelLog.HoldOutResumed(logger, modelId);
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
        catch (Exception failure) when (failure is not (OperationCanceledException or OutOfMemoryException))
        {
            // a deterministic failure ends the look: asking again would fail the same way
            await StoreTerminalAsync(model, block, key, MlHoldOutOutcome.Failed, failure.GetType().Name, mode, actor, settingHash, state, prepared.Value.Runs.Count, 0, 0, cancellationToken);
            return Result.Failure<MlHoldOutScoreView>(MlInfluenceErrors.HoldOutFailed(failure.GetType().Name));
        }

        if (ran.IsFailure)
        {
            // a version that could not be replayed (the engine reports some transient reads as failures): nothing stored, the look stays open for the window
            ModelLog.HoldOutPassFailed(logger, modelId, ran.Error.Code);
            return Result.Failure<MlHoldOutScoreView>(ran.Error);
        }

        var figures = ModelHoldOutFigures.Compute(ran.Value.Rows, ran.Value.BaseRate, Draws, Level, MinDays, $"{modelId}|{block.Start:yyyy-MM-dd}|{ran.Value.InputHash}");
        if (figures.IsFailure)
        {
            var empty = figures.Error.Code == ModelHoldOutFigures.NoRowsCode;
            await StoreTerminalAsync(model, block, key, empty ? MlHoldOutOutcome.NoRows : MlHoldOutOutcome.Failed, figures.Error.Code, mode, actor, settingHash, state, ran.Value.VersionsPlanned, ran.Value.VersionsRun, ran.Value.Dropped, cancellationToken);
            return Result.Failure<MlHoldOutScoreView>(empty ? MlInfluenceErrors.HoldOutNoRows() : MlInfluenceErrors.HoldOutFailed(figures.Error.Code));
        }

        // the setting may have been turned OFF while the pass ran: the database would refuse the automatic row, so say so here; the claimed look stays open for the owner to finish
        if (mode == Auto && !(await AutoScoreAsync(cancellationToken)).On)
        {
            return Result.Failure<MlHoldOutScoreView>(MlInfluenceErrors.InvalidLook("The automatic hold-out score was turned off while the pass ran; the claimed look stays open for the owner to finish."));
        }

        var f = figures.Value;
        var stored = new StoredMlHoldOutScore(modelId, block.Start, look.StrategyId, MlHoldOutOutcome.Scored, null, f.Rows, f.Days, f.LogLossModel, f.LogLossBaseline, f.BaselineName, f.ProbabilityBetter, f.GainLow, f.GainHigh, f.PolicyEffectR, ran.Value.InputHash, settingHash, mode, actor, time.GetUtcNow(),
            ran.Value.Dropped, block.HistoryLast, lookId, state.Id, ran.Value.VersionsPlanned, ran.Value.VersionsRun);
        try
        {
            if (!await scores.InsertAsync(stored, cancellationToken))
            {
                return Result.Failure<MlHoldOutScoreView>(MlInfluenceErrors.HoldOutReused(look.StrategyId));
            }
        }
        catch (Exception failure) when (failure is not (OperationCanceledException or OutOfMemoryException))
        {
            if (TransientFailure.Is(failure))
            {
                return Result.Failure<MlHoldOutScoreView>(MlInfluenceErrors.HoldOutInterrupted());
            }

            // a refusal by the database (a trigger, a check) is deterministic: the look ends without figures
            await StoreTerminalAsync(model, block, key, MlHoldOutOutcome.Failed, "write_refused", mode, actor, settingHash, state, ran.Value.VersionsPlanned, ran.Value.VersionsRun, ran.Value.Dropped, cancellationToken);
            return Result.Failure<MlHoldOutScoreView>(MlInfluenceErrors.HoldOutFailed("write_refused"));
        }

        var millis = (long)time.GetElapsedTime(started).TotalMilliseconds;
        ModelLog.HoldOutScored(logger, modelId, mode, f.Rows, ran.Value.Dropped, millis);
        return Result.Success(new MlHoldOutScoreView(modelId, ModelHoldOutLooks.FamilyId(model.Model), block.Start, mode, actor, f.Rows, f.Days, ran.Value.Dropped, f.LogLossModel, f.LogLossBaseline, f.BaselineName, f.ProbabilityBetter, f.GainLow, f.GainHigh, f.PolicyEffectR, settingHash, ran.Value.InputHash, stored.ScoredAt));
    }

    /// <summary>Ends the look without figures. A write that fails is logged and swallowed: the look then stays open until the resume window ends it.</summary>
    private async ValueTask StoreTerminalAsync(StoredModel model, HoldOutBlock block, string key, string outcome, string code, string mode, string actor, string settingHash, MlSwitchState state, int planned, int ran, int dropped, CancellationToken cancellationToken)
    {
        ModelLog.HoldOutTerminal(logger, model.Id, outcome, code);
        var inputHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"model-holdout-terminal/1|{model.Model.Sha256}|{block.Start:yyyy-MM-dd}|{outcome}|{code}")));
        var row = new StoredMlHoldOutScore(model.Id, block.Start, key, outcome, code.Length > 80 ? code[..80] : code, null, null, null, null, null, null, null, null, null, inputHash, settingHash, mode, actor, time.GetUtcNow(), dropped, block.HistoryLast, ModelHoldOutLooks.LookId(model.Model), state.Id, planned, ran);
        try
        {
            await scores.InsertAsync(row, cancellationToken);
        }
        catch (Exception failure) when (failure is not (OperationCanceledException or OutOfMemoryException))
        {
            ModelLog.HoldOutPassFailed(logger, model.Id, "terminal_row_refused");
        }
    }

    private async ValueTask<MlSwitchState> AutoScoreAsync(CancellationToken cancellationToken) =>
        (await switches.CurrentAsync(cancellationToken)).AutoHoldOutScore ?? new MlSwitchState(null, false);
}
