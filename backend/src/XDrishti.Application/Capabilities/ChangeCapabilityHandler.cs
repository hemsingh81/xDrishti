using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Capabilities;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Capabilities;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Capabilities;

/// <summary>
/// The owner's switch of a capability (spec P8-18 Part A, ADR 0128): the only writer of the capability log. The domain refuses a non-owner actor, a name outside the whitelist and a missing reason before anything is written. A change that the log already says is not written
/// twice. A change is audited as an <see cref="AccountEventType.CapabilityChanged"/> event naming the capability and the new state (never the reason or a password). Nothing here switches a capability by itself: no setting, rule or job calls this handler; the endpoint calls it
/// after the session check, the CSRF token and (to switch ON) a fresh password.
/// </summary>
internal sealed class ChangeCapabilityHandler(ICapabilityLogWriter writer, CapabilityViews views, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<ChangeCapabilityHandler> logger)
    : ICommandHandler<ChangeCapabilityCommand, CapabilityChangeOutcome>
{
    public async ValueTask<Result<CapabilityChangeOutcome>> HandleAsync(ChangeCapabilityCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var change = CapabilityChange.Create(command.Name, command.Enabled, command.Actor, command.Reason);
        if (change.IsFailure)
        {
            CapabilityLogEvents.Refused(logger, Domain.Capabilities.Capabilities.IsKnown(command.Name) ? command.Name : "unknown", change.Error.Code);
            return Result.Failure<CapabilityChangeOutcome>(change.Error);
        }

        var appended = await writer.AppendAsync(change.Value, cancellationToken);
        if (appended.IsFailure)
        {
            CapabilityLogEvents.Refused(logger, change.Value.Name, appended.Error.Code);
            return Result.Failure<CapabilityChangeOutcome>(appended.Error);
        }

        var state = change.Value.Enabled ? "on" : "off";
        if (appended.Value is { Created: true, Entry: { } row })
        {
            CapabilityLogEvents.Switched(logger, row.Id, change.Value.Name, state);
            events.Record(AccountEvent.Record(AccountEventType.CapabilityChanged, null, "name=" + change.Value.Name + " " + state, change.Value.Actor, clock.GetUtcNow()));
            await unitOfWork.TrySaveChangesAsync(cancellationToken);
        }
        else
        {
            CapabilityLogEvents.Repeated(logger, change.Value.Name);
        }

        return Result.Success(new CapabilityChangeOutcome(appended.Value.Created, await views.BuildAsync(cancellationToken)));
    }
}
