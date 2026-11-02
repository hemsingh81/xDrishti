using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Selection;
using XDrishti.Application.Filings;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Selection;

namespace XDrishti.Application.Selection;

/// <summary>
/// The auto option of the event-risk filter (CLAUDE.md "approvals have an auto option"; P15-06, ADR 0182): reads the newest protocol-F verdict for the current rule version through <see cref="IEventFilterVerdict"/> (the reader of the stored verdicts, P15-10; with none for the switch's N it answers unavailable and
/// nothing happens), asks the pure <see cref="EventFilterAutoDecision"/> and, for an action, appends one row <c>decided_by auto</c>, actor <c>auto:event-filter</c>, with the settings snapshot, the verdict state and the verdict reference. N and the approval setting are copied unchanged. The
/// row is never an owner approval; the account event carries the auto actor. A verdict that cannot be read is <c>unavailable</c>. It places no order; the evaluation verb runs it after each new protocol-F verdict (P15-10).
/// </summary>
internal sealed class ApplyEventFilterAutoHandler(
    IEventFilterDecisions decisions,
    IEventFilterVerdict verdicts,
    IAccountEventLog events,
    IAccountUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<ApplyEventFilterAutoHandler> logger) : ICommandHandler<ApplyEventFilterAutoCommand, EventFilterAutoResult>
{
    public async ValueTask<Result<EventFilterAutoResult>> HandleAsync(ApplyEventFilterAutoCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EventFilterVerdict? verdict = null;
        try
        {
            verdict = await verdicts.NewestAsync(EventCalendarSnapshot.RuleVersion, cancellationToken);
        }
#pragma warning disable CA1031 // A verdict that cannot be read is unavailable and changes nothing; cancellation is rethrown by the filter.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            EventFilterLog.VerdictReadFailed(logger, failure.GetType().Name);
        }

        var current = EventFilterSettings.StateOf(await decisions.RowsAsync(2, cancellationToken));
        var decision = EventFilterAutoDecision.Decide(verdict, current, EventCalendarSnapshot.RuleVersion);
        if (decision.Action == EventFilterAutoAction.None)
        {
            EventFilterLog.AutoNotApplied(logger, decision.Reason);
            return Result.Success(new EventFilterAutoResult("none", decision.Reason));
        }

        var enabled = decision.Action == EventFilterAutoAction.TurnOn;
        var snapshot = EventFilterSettings.Snapshot(enabled, current.ResultsWithinDays, current.Approval, verdict);
        var reason = enabled ? "auto: protocol F verdict KEEP for " + EventCalendarSnapshot.RuleVersion : "auto: protocol F verdict is no longer KEEP for " + EventCalendarSnapshot.RuleVersion;
        var (id, refused) = await decisions.AppendAsync(new NewEventFilterRow(enabled, current.ResultsWithinDays, current.Approval, EventFilterState.AutoDecision, EventFilterState.AutoActor, reason, snapshot, decision.VerdictReference), cancellationToken);
        if (id is not { } stored)
        {
            _ = refused;   // the database's text is not logged; the closed reason is
            EventFilterLog.AutoNotApplied(logger, "database_refused");
            return Result.Success(new EventFilterAutoResult("none", "database_refused"));
        }

        var action = enabled ? "turn_on" : "turn_off";
        EventFilterLog.AutoApplied(logger, stored, action, decision.Reason);
        try
        {
            events.Record(AccountEvent.Record(AccountEventType.EventFilterSwitched, null, $"decision={stored} enabled={(enabled ? "on" : "off")} results_within_days={current.ResultsWithinDays} approval={current.Approval} decided_by=auto", EventFilterState.AutoActor, clock.GetUtcNow()));
            await unitOfWork.TrySaveChangesAsync(CancellationToken.None);
        }
#pragma warning disable CA1031 // The row stands; the audit cross-index is a second write. Cancellation is rethrown by the filter; only the type of the failure is logged.
        catch (Exception failure) when (failure is not OperationCanceledException and not OutOfMemoryException)
#pragma warning restore CA1031
        {
            EventFilterLog.AuditFailed(logger, failure.GetType().Name);
        }

        return Result.Success(new EventFilterAutoResult(action, decision.Reason));
    }
}
