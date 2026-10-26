using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Abstractions.Capabilities;

/// <summary>
/// The second layer of a web owner act that an existing handler performs (spec P8-19, ADR 0130): it asks the capability gate again, runs ONE existing handler and, only when that handler succeeded, writes the audit cross-index row (an account event) in its own unit
/// of work. The handlers themselves stay as the command line uses them (no capability): the gate is a rule of the web path. It decides nothing about the move; the state machines of the handlers do.
/// </summary>
public interface IOwnerActRunner
{
    /// <summary>
    /// Runs the handler when <paramref name="capability"/> is ON now (else 403 <c>capabilities.off</c>, nothing called). On success records <paramref name="audit"/> with the detail built from the outcome (ids and state words only: never a reason, a number of the
    /// evidence or a password); a failure of that second write is logged by event and does not undo the move.
    /// </summary>
    ValueTask<Result<TOutcome>> RunAsync<TCommand, TOutcome>(string capability, ICommandHandler<TCommand, TOutcome> handler, TCommand command, string actor, AccountEventType audit, Func<TOutcome, string> detail, CancellationToken cancellationToken)
        where TCommand : ICommand;
}
