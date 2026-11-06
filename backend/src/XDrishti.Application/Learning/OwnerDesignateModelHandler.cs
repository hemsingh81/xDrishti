using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The use case behind <c>xd learn designate</c> (CF-10 part 10d-1 review, ADR 0220): the P6-07 designation, except that a model with a rollback proposal in any state is refused as champion (see <see cref="OwnerDesignateModelCommand"/>). Every other role, and every model without a
/// rollback proposal, goes to <see cref="DesignateModelCommand"/> unchanged. It never places an order.
/// </summary>
internal sealed class OwnerDesignateModelHandler(
    IRollbackProposalReader rollbacks,
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

        return await designate.HandleAsync(new DesignateModelCommand(command.ModelId, command.Role, command.Reason, command.Actor), cancellationToken);
    }
}
