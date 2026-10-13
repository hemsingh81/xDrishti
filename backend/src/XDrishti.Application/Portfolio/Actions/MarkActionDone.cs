using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Actions;

/// <summary>Marks an Action-center item done: it stays hidden for as long as the condition holds, and the decision is cleared when it stops holding (so a recurrence shows again). Idempotent; audited. <c>portfolio.not_found</c> for an id the rules do not produce now.</summary>
public sealed record MarkActionDoneCommand(string Id, string Actor) : ICommand;

internal sealed class MarkActionDoneHandler(ActionDecisions decisions) : ICommandHandler<MarkActionDoneCommand, ActionDecisionView>
{
    public async ValueTask<Result<ActionDecisionView>> HandleAsync(MarkActionDoneCommand command, CancellationToken cancellationToken) =>
        await decisions.DecideAsync(command.Id, ActionDecision.Done, command.Actor, cancellationToken);
}
