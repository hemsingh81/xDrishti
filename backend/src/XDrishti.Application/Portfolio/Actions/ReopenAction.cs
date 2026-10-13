using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Actions;

/// <summary>Takes a snooze or a done decision back: the item is open again. Idempotent (an item with no decision is already open) and allowed for an item the rules no longer produce; audited when it changes something. <c>portfolio.not_found</c> only for an id of no rule.</summary>
public sealed record ReopenActionCommand(string Id, string Actor) : ICommand;

internal sealed class ReopenActionHandler(ActionDecisions decisions) : ICommandHandler<ReopenActionCommand, ActionDecisionView>
{
    public async ValueTask<Result<ActionDecisionView>> HandleAsync(ReopenActionCommand command, CancellationToken cancellationToken) =>
        await decisions.DecideAsync(command.Id, ActionDecision.Cleared, command.Actor, cancellationToken);
}
