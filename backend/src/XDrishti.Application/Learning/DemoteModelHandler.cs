using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The one door of the rollback path to the model registry (spec CF-10 D8, part 10d-1, ADR 0220): it asks for the designation <see cref="ModelRole.Challenger"/> and for no other role, and only for a model that is the champion <b>at the moment of the write</b>
/// (the expected current role is checked by the registry under its lock): a model that was retired in between stays retired and the answer says nothing changed. An architecture test reads this source and the IL: it is the only caller of the designation in the closure
/// of the automatic rollback, and it names no target role but Challenger.
/// </summary>
internal sealed class DemoteModelHandler(ICommandHandler<DesignateModelCommand, DesignationOutcome> designate) : ICommandHandler<DemoteModelCommand, DesignationOutcome>
{
    public ValueTask<Result<DesignationOutcome>> HandleAsync(DemoteModelCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return designate.HandleAsync(new DesignateModelCommand(command.ModelId, ModelRole.Challenger, command.Reason, command.Actor, command.Evidence, ExpectedCurrentRole: ModelRole.Champion), cancellationToken);
    }
}
