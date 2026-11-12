using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The use case behind <c>xd learn designate</c> (CF-10 part 10d-1 review, ADR 0220): the P6-07 designation, except that a model with a rollback proposal in any state is refused as champion (see <see cref="OwnerDesignateModelCommand"/>). Every other role, and every model without a
/// rollback proposal, goes to <see cref="DesignateModelCommand"/> unchanged, except that naming a champion once any model has ever been champion goes through <see cref="PromoteChallengerCommand"/> (CF-10e decision D6, ADR 0281): the promotion rule and the predicate's
/// evidence decide, not the champion rule alone. The command line is the owner's local act, so its actor is passed on as <c>owner:</c> plus the actor. With no champion in the history (the first champion) the champion rule alone applies, as before. It never places an order.
/// </summary>
internal sealed class OwnerDesignateModelHandler(
    IModelRegistry registry,
    IRollbackProposalReader rollbacks,
    ICommandHandler<PromoteChallengerCommand, PromotionOutcome> promote,
    ICommandHandler<DesignateModelCommand, DesignationOutcome> designate,
    ILogger<OwnerDesignateModelHandler> logger) : ICommandHandler<OwnerDesignateModelCommand, DesignationOutcome>
{
    public async ValueTask<Result<DesignationOutcome>> HandleAsync(OwnerDesignateModelCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Role == ModelRole.Champion && (await rollbacks.ListAsync(null, command.ModelId, 1, cancellationToken)).Count > 0)
        {
            MlSafetyLog.Refused(logger, MlSafetyErrors.DesignateRolledBackCode);
            return Result.Failure<DesignationOutcome>(MlSafetyErrors.DesignateRolledBack(command.ModelId));
        }

        if (command.Role == ModelRole.Champion)
        {
            // the first champion ever is named on the champion rule alone; once any model has been champion (a current one, or one retired or demoted since) a champion is named only through promotion (CF-10e D6, ADR 0281)
            if (await registry.FindAsync(command.ModelId, cancellationToken) is { Role: not ModelRole.Champion } && await registry.AnyChampionEverAsync(cancellationToken))
            {
                var promoted = await promote.HandleAsync(new PromoteChallengerCommand(command.ModelId, MlActors.OwnerPrefix + command.Actor, command.Reason), cancellationToken);
                return promoted.IsFailure
                    ? Result.Failure<DesignationOutcome>(promoted.Error)
                    : Result.Success(new DesignationOutcome(promoted.Value.ModelId, promoted.Value.Role, promoted.Value.Changed));
            }
        }

        return await designate.HandleAsync(new DesignateModelCommand(command.ModelId, command.Role, command.Reason, command.Actor), cancellationToken);
    }
}
