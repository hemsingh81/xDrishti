using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Portfolio.Actions;

/// <summary>Hides an Action-center item for 7 days (it shows again on that India date if the rules still produce it). Idempotent; audited. <c>portfolio.not_found</c> for an id the rules do not produce now.</summary>
public sealed record SnoozeActionCommand(string Id, string Actor) : ICommand;

internal sealed class SnoozeActionHandler(ActionDecisions decisions) : ICommandHandler<SnoozeActionCommand, ActionDecisionView>
{
    public async ValueTask<Result<ActionDecisionView>> HandleAsync(SnoozeActionCommand command, CancellationToken cancellationToken) =>
        await decisions.DecideAsync(command.Id, ActionDecision.Snoozed, command.Actor, cancellationToken);
}
