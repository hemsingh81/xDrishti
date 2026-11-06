using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The daily ML monitor over every model (spec CF-10 D7 and D8, part 10d-1, ADR 0220). Rollback: each champion is evaluated once for today by <see cref="ProposeRollbackCommand"/>, which is idempotent per model and day. Promotion: only when <c>ml.auto_promote</c> is ON, each challenger
/// is offered to <see cref="PromoteChallengerCommand"/> as the actor <c>auto</c>, which refuses it unless the promotion rule and the predicate without the switch hold (no source of that evidence exists yet, so nothing is promoted). One model that fails or throws does not stop the others
/// and records nothing for itself, so the next run repeats it. With the switches OFF (the default) the run writes only its own proposals and evaluations. It re-promotes nothing, sets no switch and places no order.
/// </summary>
internal sealed class EvaluateMlModelsHandler(
    IModelRegistry registry,
    IMlSwitchSettings switches,
    ICommandHandler<ProposeRollbackCommand, ProposeRollbackOutcome> propose,
    ICommandHandler<PromoteChallengerCommand, PromotionOutcome> promote,
    ILogger<EvaluateMlModelsHandler> logger) : ICommandHandler<EvaluateMlModelsCommand, MlRunOutcome>
{
    private const int Limit = 200;

    public async ValueTask<Result<MlRunOutcome>> HandleAsync(EvaluateMlModelsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var champions = await registry.ListAsync(ModelRole.Champion, Limit, cancellationToken);
        int evaluated = 0, already = 0, proposed = 0, rolled = 0, promoted = 0, notPromoted = 0, failed = 0;
        foreach (var champion in champions)
        {
            Result<ProposeRollbackOutcome> result;
            try
            {
                result = await propose.HandleAsync(new ProposeRollbackCommand(champion.Id), cancellationToken);
            }
#pragma warning disable CA1031 // One model that throws (a store that is down, a refused write) must not stop the others; it is counted, logged by its type and repeated on the next run.
            catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
            {
                failed++;
                MlSafetyLog.ItemThrew(logger, failure.GetType().Name);
                continue;
            }

            if (result.IsFailure)
            {
                failed++;
                MlSafetyLog.ItemFailed(logger, result.Error.Code);
                continue;
            }

            rolled += result.Value.AppliedAutomatically;
            failed += result.Value.ApplyFailures > 0 ? 1 : 0;
            if (result.Value.AlreadyChecked)
            {
                already++;
                continue;
            }

            evaluated++;
            proposed += result.Value.ProposalIds.Count;
        }

        if ((await switches.CurrentAsync(cancellationToken)).AutoPromote.On)
        {
            foreach (var challenger in await registry.ListAsync(ModelRole.Challenger, Limit, cancellationToken))
            {
                try
                {
                    var result = await promote.HandleAsync(new PromoteChallengerCommand(challenger.Id, MlActors.Auto, "automatic promotion"), cancellationToken);
                    if (result.IsSuccess)
                    {
                        promoted += result.Value.Changed ? 1 : 0;
                    }
                    else
                    {
                        notPromoted++; // a refusal is the rule's answer (the evidence is not there, or the model does not qualify), not a failure
                    }
                }
#pragma warning disable CA1031 // One challenger that throws must not stop the others; it is counted, logged by its type and considered again on the next run.
                catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
                {
                    failed++;
                    MlSafetyLog.ItemThrew(logger, failure.GetType().Name);
                }
            }
        }

        MlSafetyLog.Run(logger, champions.Count, evaluated, already, proposed, rolled, promoted, failed);
        return Result.Success(new MlRunOutcome(champions.Count, evaluated, already, proposed, rolled, promoted, notPromoted, failed));
    }
}
