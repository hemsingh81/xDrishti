using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Auth;
using XDrishti.Application.Filings;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Selection;

/// <summary>
/// The owner's change of the event-risk switch (P15-06, ADR 0182). Order: the actor is the signed-in owner; <c>enabled</c> is given; N is 0 to 10 and the approval is manual or auto; the reason is 1 to 300 characters without secrets; then the fresh password for THIS act (ON and
/// OFF alike, the elevation window untouched; 401 / 423 from the step-up); then one row appended as <c>decided_by owner</c> with the settings snapshot, which the database judges again; then the account event <c>EventFilterSwitched</c>. Nothing here places, sizes, moves or cancels an order.
/// </summary>
internal sealed class ChangeEventFilterHandler(
    IEventFilterDecisions decisions,
    ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
    IQueryHandler<GetEventFilterQuery, EventFilterView> view,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<ChangeEventFilterHandler> logger) : ICommandHandler<ChangeEventFilterCommand, EventFilterView>
{
    public async ValueTask<Result<EventFilterView>> HandleAsync(ChangeEventFilterCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!EventFilterActRules.IsOwner(command.Actor))
        {
            return Refuse(Error.Forbidden(EventFilterErrors.OwnerOnly, "Only the signed-in owner can change the event-risk filter."));
        }

        if (command.Enabled is not { } enabled)
        {
            return Refuse(Error.Validation(EventFilterErrors.InvalidSetting, "enabled: true or false."));
        }

        var days = command.ResultsWithinDays;
        var approval = command.Approval;
        if (days is { } d && !ResultsWindow.IsValidDays(d))
        {
            return Refuse(Error.Validation(EventFilterErrors.InvalidSetting, "resultsWithinDays: 0 to 10."));
        }

        if (approval is not null && !EventFilterState.IsApproval(approval))
        {
            return Refuse(Error.Validation(EventFilterErrors.InvalidSetting, "approval: manual or auto."));
        }

        if (!EventFilterActRules.ReasonOk(command.Reason))
        {
            return Refuse(Error.Validation(EventFilterErrors.ReasonInvalid, "reason: 1 to 300 characters without secrets."));
        }

        var checkedPassword = await passwordCheck.HandleAsync(new StepUpCommand(command.SessionId, command.Password, command.Client, Elevate: false), cancellationToken);
        if (checkedPassword.IsFailure)
        {
            return Refuse(checkedPassword.Error);
        }

        // The values left out are the ones in force now, read after the password so that a wrong password learns nothing.
        var rows = await decisions.RowsAsync(2, cancellationToken);
        var current = EventFilterSettings.StateOf(rows);
        var newDays = days ?? current.ResultsWithinDays;
        var newApproval = approval ?? current.Approval;
        var reason = command.Reason!.Trim();
        var snapshot = EventFilterSettings.Snapshot(enabled, newDays, newApproval);
        var (id, refused) = await decisions.AppendAsync(new NewEventFilterRow(enabled, newDays, newApproval, EventFilterState.OwnerDecision, command.Actor, reason, snapshot, null), cancellationToken);
        if (id is not { } stored)
        {
            return Refuse(Error.Conflict(EventFilterErrors.Stale, "The change was not stored: " + (refused ?? "the database refused the row") + ". Reload and try again."));
        }

        EventFilterLog.Switched(logger, stored, enabled, newDays, newApproval, EventFilterState.OwnerDecision);
        try
        {
            events.Record(AccountEvent.Record(AccountEventType.EventFilterSwitched, null, $"decision={stored} enabled={(enabled ? "on" : "off")} results_within_days={newDays} approval={newApproval} decided_by=owner", command.Actor, clock.GetUtcNow()));
            await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }
#pragma warning disable CA1031 // The row stands; the audit cross-index is a second write. Cancellation is rethrown by the filter; only the type of the failure is logged.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            EventFilterLog.AuditFailed(logger, failure.GetType().Name);
        }

        return Result.Success(await view.HandleAsync(new GetEventFilterQuery(), cancellationToken));
    }

    private Result<EventFilterView> Refuse(Error error)
    {
        EventFilterLog.SwitchRefused(logger, error.Code);
        return Result.Failure<EventFilterView>(error);
    }
}
