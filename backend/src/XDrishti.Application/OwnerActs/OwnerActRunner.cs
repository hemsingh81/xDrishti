using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Capabilities;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.OwnerActs;

/// <summary>The one place where a web owner act of P8-19 passes the gate again and is audited (ADR 0130). Calls exactly the handler it is given, once.</summary>
internal sealed class OwnerActRunner(ICapabilityGate gate, IAccountEventLog events, IAccountUnitOfWork unitOfWork, TimeProvider clock, ILogger<OwnerActRunner> logger) : IOwnerActRunner
{
    public async ValueTask<Result<TOutcome>> RunAsync<TCommand, TOutcome>(string capability, ICommandHandler<TCommand, TOutcome> handler, TCommand command, string actor, AccountEventType audit, Func<TOutcome, string> detail, CancellationToken cancellationToken)
        where TCommand : ICommand
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(detail);
        var name = audit.ToString();
        var open = await gate.RequireAsync(capability, cancellationToken);
        if (open.IsFailure)
        {
            OwnerActLogEvents.RefusedOff(logger, capability);
            return Result.Failure<TOutcome>(open.Error);
        }

        var result = await handler.HandleAsync(command, cancellationToken);
        if (result.IsFailure)
        {
            return result;
        }

        // The move is written and stands: a client that went away now must not cancel its audit row, so the second write ignores the request token.
        // The move is written and stands. The cross-index row is a second, separate write: when it fails it is logged and the move is not undone (it cannot be: the rows are insert-only).
        try
        {
            events.Record(AccountEvent.Record(audit, null, detail(result.Value), actor, clock.GetUtcNow()));
            if (await unitOfWork.TrySaveChangesAsync(CancellationToken.None))
            {
                OwnerActLogEvents.Done(logger, name);
                return result;
            }
        }
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
        {
            // Falls through to the log below.
        }

        OwnerActLogEvents.AuditFailed(logger, name);
        return result;
    }
}
