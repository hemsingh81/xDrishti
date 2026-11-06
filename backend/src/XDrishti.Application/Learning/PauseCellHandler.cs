using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Learning;

/// <summary>
/// The pause of a cell (spec CF-12 part 12b, ADR 0219): the lifecycle's own recorder with the target fixed to <c>Paused</c>. It states no fact, so only a pause the table allows (Paper or Active to Paused) is recorded; any other state is refused by the table. An
/// architecture test pins that the automatic path reaches the lifecycle only through this handler and that this file names no other target state.
/// </summary>
internal sealed class PauseCellHandler(ICommandHandler<RecordCellTransitionCommand, ChangelogOutcome> lifecycle) : ICommandHandler<PauseCellCommand, ChangelogOutcome>
{
    public ValueTask<Result<ChangelogOutcome>> HandleAsync(PauseCellCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        return lifecycle.HandleAsync(new RecordCellTransitionCommand(command.Cell, CellState.Paused, command.Reason, command.Actor, new TransitionFacts(), command.EvidenceIds, command.BeforeRef, command.AfterRef), cancellationToken);
    }
}
