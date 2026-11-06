using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// Promotion and re-promotion of a model to champion (spec CF-10 D7 and D8, part 10d-1, ADR 0220). The registry's designation and the changelog row are the P6-07 / P6-08 path (<see cref="DesignateModelCommand"/>), reused as is; this handler decides whether it may be asked for.
/// <list type="bullet">
/// <item>A <b>challenger</b> is promoted only if <see cref="MlPromotion"/> holds on the figures of <see cref="IChallengerEvidence"/> (no source exists today, so every promotion is refused with the reason: an unavailable figure is never a pass).</item>
/// <item>A <b>retired</b> model (the previous champion after a rollback) is re-promoted only by the owner, and only if its latest evaluation still satisfies the influence predicate without the switch.</item>
/// <item>The <b>automatic</b> actor needs <c>ml.auto_promote</c> ON, may promote only a challenger that has no rollback proposal of its own, and needs both the promotion rule and the predicate without the switch to hold. It is recorded as <c>auto</c> with the switch row and the settings hash, never as an owner.</item>
/// </list>
/// It reads <c>ml.influence.enabled</c> nowhere and writes no switch: promotion never turns influence on.
/// </summary>
internal sealed class PromoteChallengerHandler(
    IModelRegistry registry,
    IChallengerEvidence evidence,
    MlInfluenceEvaluator evaluator,
    IMlSwitchSettings switches,
    IRollbackProposalReader rollbacks,
    ICommandHandler<DesignateModelCommand, DesignationOutcome> designate,
    MlInfluenceOptions options,
    ILogger<PromoteChallengerHandler> logger) : ICommandHandler<PromoteChallengerCommand, PromotionOutcome>
{
    public async ValueTask<Result<PromotionOutcome>> HandleAsync(PromoteChallengerCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var auto = command.Actor == MlActors.Auto;
        if (!auto && !MlActors.IsOwner(command.Actor))
        {
            MlSafetyLog.Refused(logger, MlSafetyErrors.OwnerOnlyCode);
            return Result.Failure<PromotionOutcome>(MlSafetyErrors.OwnerOnly());
        }

        var model = await registry.FindAsync(command.ModelId, cancellationToken);
        if (model is null)
        {
            MlSafetyLog.Refused(logger, ModelErrors.NotFoundCode);
            return Result.Failure<PromotionOutcome>(ModelErrors.NotFound(command.ModelId));
        }

        if (model.Role == ModelRole.Champion)
        {
            return Result.Success(new PromotionOutcome(model.Id, ModelRole.Champion.Code(), false, "already_champion", command.Actor));
        }

        var current = await switches.CurrentAsync(cancellationToken);
        if (auto)
        {
            var refused = await AutomaticRefusalAsync(model, current, cancellationToken);
            if (refused is not null)
            {
                return Result.Failure<PromotionOutcome>(refused);
            }
        }

        var evaluation = await evaluator.EvaluateAsync(model.Id, cancellationToken);
        if (evaluation is null)
        {
            return Result.Failure<PromotionOutcome>(ModelErrors.NotFound(command.ModelId));
        }

        var repromotion = model.Role == ModelRole.Retired;
        if (repromotion)
        {
            if (!evaluation.Result.HoldsWithoutSwitch)
            {
                return Refuse(MlSafetyErrors.RepromotionRefused(model.Id, NotHolding(evaluation.Result)));
            }
        }
        else
        {
            var champions = await registry.ListAsync(ModelRole.Champion, 1, cancellationToken);
            var champion = champions.Count > 0 ? champions[0] : null;
            var figures = await evidence.ReadAsync(model, champion, cancellationToken);
            var m = model.Model;
            var facts = new MlPromotionFacts(figures.GradedCandidates, figures.Weeks, figures.ProbabilityBeatsChampion, figures.Counterfactual, ChampionRule.Reasons(m.Verdict, m.HoldOutStart, m.UsedFolds, m.Trials, model.RequiredTrials));
            var rule = MlPromotion.Evaluate(facts, options);
            if (!rule.MayPromote)
            {
                return Refuse(MlSafetyErrors.PromotionRefused(model.Id, rule.Reasons));
            }

            // an automatic promotion needs the predicate's own terms too (the same evidence that would let the model influence a decision, except the owner's switch)
            if (auto && !evaluation.Result.HoldsWithoutSwitch)
            {
                return Refuse(MlSafetyErrors.PromotionRefused(model.Id, NotHolding(evaluation.Result)));
            }
        }

        var kind = repromotion ? "re_promotion" : "promotion";
        var reason = auto ? $"automatic promotion; ml.auto_promote switch row {current.AutoPromote.Id}; settings {evaluation.SettingHash[..12]}" : command.Reason?.Trim() ?? string.Empty;
        var trail = auto ? new[] { $"ml-switch:{current.AutoPromote.Id}", "settings:" + evaluation.SettingHash[..16] } : [];
        var designated = await designate.HandleAsync(new DesignateModelCommand(model.Id, ModelRole.Champion, reason, command.Actor, trail), cancellationToken);
        if (designated.IsFailure)
        {
            MlSafetyLog.Refused(logger, designated.Error.Code);
            return Result.Failure<PromotionOutcome>(designated.Error);
        }

        if (designated.Value.Changed)
        {
            MlSafetyLog.Promoted(logger, model.Id, auto ? "auto" : "owner");
        }

        return Result.Success(new PromotionOutcome(model.Id, designated.Value.Role, designated.Value.Changed, kind, command.Actor));
    }

    /// <summary>Why the automatic actor may not even try this model, or null.</summary>
    private async ValueTask<Error?> AutomaticRefusalAsync(StoredModel model, MlSwitches current, CancellationToken cancellationToken)
    {
        if (!current.AutoPromote.On)
        {
            MlSafetyLog.Refused(logger, MlSafetyErrors.AutoPromoteOffCode);
            return MlSafetyErrors.AutoPromoteOff();
        }

        if (model.Role != ModelRole.Challenger)
        {
            // re-promotion is always the owner's act
            MlSafetyLog.Refused(logger, MlSafetyErrors.OwnerOnlyCode);
            return MlSafetyErrors.OwnerOnly();
        }

        if ((await rollbacks.ListAsync(null, model.Id, 1, cancellationToken)).Count > 0)
        {
            // a model that was proposed for rollback (applied or not) comes back only by the owner's act
            MlSafetyLog.Refused(logger, MlSafetyErrors.OwnerOnlyCode);
            return MlSafetyErrors.OwnerOnly();
        }

        return null;
    }

    private static List<MlInfluenceReason> NotHolding(MlInfluenceResult result) => [.. result.Terms.Where(t => t.Name != MlInfluence.SwitchOn && !t.Holds).SelectMany(t => t.Reasons)];

    private Result<PromotionOutcome> Refuse(Error error)
    {
        MlSafetyLog.Refused(logger, error.Code);
        return Result.Failure<PromotionOutcome>(error);
    }
}
